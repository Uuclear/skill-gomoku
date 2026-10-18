// Package room implements the game room aggregate: two peers, the
// authoritative Engine, start/restart flow, action dispatch, timer checks,
// disconnect grace and full-snapshot broadcasts.
package room

import (
	"encoding/json"
	"errors"
	"sync"
	"time"

	"chess-server/internal/game"
	"chess-server/internal/proto"
	"chess-server/internal/skill"
)

// Peer is the network-facing view of one connected player.
type Peer interface {
	ID() string
	Nickname() string
	Send(proto.Message)
}

const (
	KindFriend = "friend"
	KindMatch  = "match"

	StateWaiting = "waiting"
	StatePlaying = "playing"
	StateOver    = "over"

	GraceMs   = 30_000
	WarnAtSec = 10
)

var (
	ErrRoomFull     = errors.New("room is full")
	ErrRoomCode     = errors.New("room not found")
	ErrBadLoadout   = errors.New("invalid skill loadout")
	ErrNotInRoom    = errors.New("not a room member")
	ErrNotHost      = errors.New("host only")
	ErrNotReady     = errors.New("opponent not ready")
	ErrAlreadyOver  = errors.New("room not waiting")
	ErrAlreadyInGame = errors.New("already in a room or queue")
)

// Member tracks one seat. id is retained so a seat survives a disconnect
// (peer becomes nil) without losing identity.
type Member struct {
	id        string
	peer      Peer
	color     int
	nickname  string
	skills    []int
	ready     bool
	offlineAt int64 // 0 = online; unix ms when disconnected mid-game
}

func (m *Member) online() bool { return m.peer != nil }

// Room is one game session. All game-action methods are safe for concurrent
// use; the engine is only touched while mu is held. Lobby mutations are driven
// by the Manager (lock order: Manager.mu then Room.mu).
type Room struct {
	ID   string
	Code string
	Kind string

	mu       sync.Mutex
	state    string
	hostID   string
	members  map[string]*Member
	order    []string // player IDs in seat order (host/black first)
	engine   *game.Engine
	executor *skill.Executor

	now        func() int64 // injectable unix-ms clock
	warnSent   bool
	abandoned  bool // ended via disconnect grace
	overReason string
	ticker     *time.Ticker
	done       chan struct{}
	createdAt  int64
	endedAt    int64 // unix ms when the game ended (for stale cleanup)
	rematch    map[string]bool

	manager *Manager
}

func nowMs() int64 { return time.Now().UnixMilli() }

func mustMarshal(v any) json.RawMessage {
	b, _ := json.Marshal(v)
	return b
}

// validLoadout requires exactly 4 distinct known skill ids.
func validLoadout(ids []int) bool {
	if len(ids) != 4 {
		return false
	}
	seen := map[int]bool{}
	for _, id := range ids {
		if _, ok := skill.ByID(id); !ok || seen[id] {
			return false
		}
		seen[id] = true
	}
	return true
}

func (r *Room) memberByColor(c int) *Member {
	for _, m := range r.members {
		if m.color == c {
			return m
		}
	}
	return nil
}

func (r *Room) other(p *Member) *Member {
	for _, m := range r.members {
		if m != p {
			return m
		}
	}
	return nil
}

// State returns the room state.
func (r *Room) State() string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.state
}

// EndedAt returns when the game ended (0 while it has not).
func (r *Room) EndedAt() int64 {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.endedAt
}

// HasMember reports whether id occupies a seat.
func (r *Room) HasMember(id string) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	_, ok := r.members[id]
	return ok
}

// MemberIDs returns the current seat IDs.
func (r *Room) MemberIDs() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.order...)
}

// ---------- lobby (waiting room); called by Manager under Manager.mu ----------

// addHostLocked seats the room creator.
func (r *Room) addHostLocked(p Peer, skills []int) {
	r.members[p.ID()] = &Member{
		id: p.ID(), peer: p, color: game.Black, nickname: p.Nickname(),
		skills: append([]int(nil), skills...), ready: true,
	}
	r.order = []string{p.ID()}
	r.hostID = p.ID()
}

// addGuestLocked seats the second player.
func (r *Room) addGuestLocked(p Peer, skills []int) {
	r.members[p.ID()] = &Member{
		id: p.ID(), peer: p, color: game.White, nickname: p.Nickname(),
		skills: append([]int(nil), skills...),
	}
	r.order = append(r.order, p.ID())
}

// joinLocked adds the second player (friend rooms). Caller holds Manager.mu.
func (r *Room) joinLocked(p Peer, skills []int) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StateWaiting || len(r.members) >= 2 {
		return ErrRoomFull
	}
	if _, exists := r.members[p.ID()]; exists {
		return ErrRoomFull
	}
	r.addGuestLocked(p, skills)
	r.broadcastRoomStateLocked()
	return nil
}

// setReadyLocked marks a member ready. Caller holds Manager.mu.
func (r *Room) setReadyLocked(id string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	m, ok := r.members[id]
	if !ok || r.state != StateWaiting {
		return ErrNotInRoom
	}
	m.ready = true
	r.broadcastRoomStateLocked()
	return nil
}

// startLocked begins the game: builds engine/executor, notifies both peers
// with game_start and pushes the first viewer-specific snapshot. Caller holds
// Manager.mu for lobby entry points; rematch calls it under Room.mu only.
func (r *Room) startLocked() {
	blackID := r.order[0]
	whiteID := r.order[1]
	bm, wm := r.members[blackID], r.members[whiteID]
	bSkills := append([]int(nil), bm.skills...)
	wSkills := append([]int(nil), wm.skills...)
	bm.color = game.Black
	wm.color = game.White
	r.engine = game.NewEngine(bSkills, wSkills, r.now())
	r.executor = skill.NewExecutor(r.engine)
	r.state = StatePlaying
	r.warnSent = false
	r.abandoned = false
	r.overReason = ""
	r.endedAt = 0
	r.rematch = map[string]bool{}

	cfg := proto.GameConfig{
		StartEnergy: r.engine.Cfg.StartEnergy,
		Gain:        r.engine.Cfg.Gain,
		MaxEnergy:   r.engine.Cfg.MaxEnergy,
		TurnSeconds: r.engine.Cfg.TurnSeconds,
		BoardSize:   game.BoardSize,
	}
	for _, m := range r.members {
		if m.online() {
			m.peer.Send(proto.Envelope("game_start", 0, proto.GameStartMsg{
				RoomID:      r.ID,
				BoardSize:   game.BoardSize,
				YourColor:   m.color,
				BlackSkills: bSkills,
				WhiteSkills: wSkills,
				Config:      cfg,
			}))
		}
	}
	r.broadcastSnapshotLocked()
	r.startTimerLocked()
}

// Start begins a friend room once both seats are filled and the guest is ready.
func (r *Room) Start(host Peer) error {
	r.manager.mu.Lock()
	defer r.manager.mu.Unlock()
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StateWaiting {
		return ErrAlreadyOver
	}
	if r.hostID != host.ID() {
		return ErrNotHost
	}
	if len(r.members) < 2 {
		return ErrNotInRoom
	}
	guest := r.order[1]
	if r.Kind == KindFriend && !r.members[guest].ready {
		return ErrNotReady
	}
	r.startLocked()
	return nil
}

func (r *Room) roomStateLocked() proto.RoomStateMsg {
	players := make([]proto.RoomPlayerInfo, 0, len(r.order))
	for _, id := range r.order {
		m := r.members[id]
		players = append(players, proto.RoomPlayerInfo{
			ID:       m.id,
			Nickname: m.nickname,
			Ready:    m.ready,
			SkillCnt: len(m.skills),
			IsHost:   id == r.hostID,
		})
	}
	return proto.RoomStateMsg{RoomCode: r.Code, HostID: r.hostID, Players: players}
}

func (r *Room) broadcastRoomStateLocked() {
	msg := proto.Envelope("room_state", 0, r.roomStateLocked())
	for _, m := range r.members {
		if m.online() {
			m.peer.Send(msg)
		}
	}
}

// sendRoomState notifies one peer of the current lobby state.
func (r *Room) sendRoomState(p Peer) {
	r.mu.Lock()
	defer r.mu.Unlock()
	p.Send(proto.Envelope("room_state", 0, r.roomStateLocked()))
}

// removeMemberLocked deletes a seat and notifies a survivor. Returns true when
// the room became empty. Caller holds Room.mu (and Manager.mu from the hub).
func (r *Room) removeMemberLocked(id string, notify bool) bool {
	delete(r.members, id)
	for i, x := range r.order {
		if x == id {
			r.order = append(r.order[:i], r.order[i+1:]...)
			break
		}
	}
	empty := len(r.members) == 0
	if !empty && notify {
		for _, m := range r.members {
			if m.online() {
				m.peer.Send(proto.Envelope("opponent_left", 0, proto.OpponentLeftMsg{Reason: "left"}))
			}
		}
	}
	return empty
}

// ---------- active game ----------

// Move applies a placement intent.
func (r *Room) Move(playerID string, x, y int) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StatePlaying {
		return ErrNotInRoom
	}
	m, ok := r.members[playerID]
	if !ok || !m.online() {
		return ErrNotInRoom
	}
	res, err := r.engine.ApplyMove(m.color, x, y, r.now())
	if err != nil {
		return err
	}
	r.warnSent = false
	r.dispatchResultLocked(res)
	return nil
}

// UseSkill applies a skill intent.
func (r *Room) UseSkill(playerID string, skillID int, targets []proto.Point, dir int) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StatePlaying {
		return ErrNotInRoom
	}
	m, ok := r.members[playerID]
	if !ok || !m.online() {
		return ErrNotInRoom
	}
	res, err := r.executor.Use(skill.Request{
		Color:   m.color,
		SkillID: skillID,
		Targets: targets,
		Dir:     dir,
		NowMs:   r.now(),
	})
	if err != nil {
		return err
	}
	// skill use does not reset the turn timer; only placements do.
	r.dispatchResultLocked(res)
	return nil
}

// Surrender ends the game for the requesting side.
func (r *Room) Surrender(playerID string) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StatePlaying {
		return ErrNotInRoom
	}
	m, ok := r.members[playerID]
	if !ok || !m.online() {
		return ErrNotInRoom
	}
	w := r.engine.Surrender(m.color)
	r.finishLocked(w, "surrender", nil)
	return nil
}

// Emote broadcasts one of the fixed emotes.
func (r *Room) Emote(playerID string, id int) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StatePlaying {
		return ErrNotInRoom
	}
	if id < 0 || id > 5 {
		return errors.New("bad emote")
	}
	m, ok := r.members[playerID]
	if !ok {
		return ErrNotInRoom
	}
	msg := proto.Envelope("emote", 0, proto.EmoteMsg{FromColor: m.color, ID: id})
	for _, x := range r.members {
		if x.online() {
			x.peer.Send(msg)
		}
	}
	return nil
}

// RematchVote handles accept/refuse of a rematch. Both accepting reuses the
// same loadouts and restarts immediately. Refusal dissolves the room to lobby.
func (r *Room) RematchVote(playerID string, accept bool) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StateOver || r.abandoned {
		return ErrNotInRoom
	}
	m, ok := r.members[playerID]
	if !ok {
		return ErrNotInRoom
	}
	if !accept {
		for _, x := range r.members {
			if x != m && x.online() {
				x.peer.Send(proto.Envelope("opponent_left", 0, proto.OpponentLeftMsg{Reason: "left"}))
			}
		}
		r.state = StateWaiting
		r.endedAt = 0
		r.stopTimerLocked()
		return nil
	}
	r.rematch[playerID] = true
	if len(r.rematch) < 2 {
		for _, x := range r.members {
			if x != m && x.online() {
				x.peer.Send(proto.Envelope("rematch_requested", 0, proto.RematchRequestedMsg{FromColor: m.color}))
			}
		}
		return nil
	}
	r.startLocked()
	return nil
}

// dispatchResultLocked fans an action result out: each public event with a
// viewer-specific snapshot, private payloads, and game_over when applicable.
func (r *Room) dispatchResultLocked(res *game.ActionResult) {
	for _, ev := range res.Events {
		for _, m := range r.members {
			if !m.online() {
				continue
			}
			// Hide mine locations from the opponent: the mine_plant event and
			// the generic skill event for skill 5 both carry the planted point.
			if ev.Actor != m.color {
				if ev.Kind == "mine_plant" {
					continue
				}
				if ev.Kind == "skill" && ev.SkillID == 5 {
					ev.X, ev.Y, ev.Points = 0, 0, nil
				}
			}
			msg := proto.Envelope("game_event", 0, proto.GameEventMsg{
				Event: ev, State: r.snapshotForLocked(m.color),
			})
			m.peer.Send(msg)
		}
	}
	for _, pv := range res.Private {
		if m := r.memberByColor(pv.Color); m != nil && m.online() {
			m.peer.Send(proto.Envelope("skill_private", 0, proto.PrivateSkillMsg{
				SkillID: pv.SkillID, Payload: mustMarshal(pv.Payload),
			}))
		}
	}
	if res.Over {
		r.finishLocked(res.Winner, res.Reason, res.WinLine)
	}
}

func (r *Room) finishLocked(winner *int, reason string, winLine []proto.Point) {
	if r.state == StateOver {
		return
	}
	r.state = StateOver
	r.overReason = reason
	r.endedAt = r.now()
	over := proto.GameOverMsg{Winner: winner, Reason: reason, WinLine: winLine}
	msg := proto.Envelope("game_over", 0, over)
	for _, m := range r.members {
		if m.online() {
			m.peer.Send(msg)
		}
	}
	r.stopTimerLocked()
}

// snapshotForLocked returns the viewer-specific snapshot: without active
// 侦查 the opponent's energy/cooldowns are masked; own view is full.
func (r *Room) snapshotForLocked(viewer int) proto.Snapshot {
	s := r.engine.Snapshot(viewer)
	viewerSide := r.engine.Side(viewer)
	if viewerSide.ScoutTurns <= 0 {
		if viewer == game.Black {
			maskSide(&s.White)
		} else {
			maskSide(&s.Black)
		}
	}
	return s
}

func maskSide(d *proto.SideStateDTO) {
	d.Energy = -1
	for i := range d.Skills {
		d.Skills[i].CD = -1
	}
}

func (r *Room) broadcastSnapshotLocked() {
	for _, m := range r.members {
		if m.online() {
			s := r.snapshotForLocked(m.color)
			m.peer.Send(proto.Envelope("game_event", 0, proto.GameEventMsg{
				Event: proto.GameEvent{Kind: "turn_start", Actor: r.engine.Turn},
				State: s,
			}))
		}
	}
}

// ---------- disconnect / reconnect ----------

// Detach marks a player offline mid-game (or post-game). The clock keeps
// running; the opponent is notified immediately. Reconnect within the grace
// window restores the session via Attach.
func (r *Room) Detach(playerID string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	m, ok := r.members[playerID]
	if !ok || !m.online() {
		return
	}
	playing := r.state == StatePlaying
	m.peer = nil
	if playing {
		m.offlineAt = r.now()
		if o := r.other(m); o != nil && o.online() {
			o.peer.Send(proto.Envelope("opponent_left", 0, proto.OpponentLeftMsg{Reason: "disconnect"}))
		}
	}
}

// Attach rebinds a reconnecting peer and resends the current state. Returns
// false if the player is not a member.
func (r *Room) Attach(p Peer) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	m, ok := r.members[p.ID()]
	if !ok {
		return false
	}
	m.peer = p
	m.offlineAt = 0
	bSkills, wSkills := r.skillListsLocked()
	switch r.state {
	case StateWaiting:
		p.Send(proto.Envelope("room_state", 0, r.roomStateLocked()))
	case StatePlaying, StateOver:
		p.Send(proto.Envelope("game_start", 0, proto.GameStartMsg{
			RoomID:      r.ID,
			BoardSize:   game.BoardSize,
			YourColor:   m.color,
			BlackSkills: bSkills,
			WhiteSkills: wSkills,
			Config: proto.GameConfig{
				StartEnergy: r.engine.Cfg.StartEnergy,
				Gain:        r.engine.Cfg.Gain,
				MaxEnergy:   r.engine.Cfg.MaxEnergy,
				TurnSeconds: r.engine.Cfg.TurnSeconds,
				BoardSize:   game.BoardSize,
			},
		}))
		p.Send(proto.Envelope("game_event", 0, proto.GameEventMsg{
			Event: proto.GameEvent{Kind: "turn_start", Actor: r.engine.Turn},
			State: r.snapshotForLocked(m.color),
		}))
		if r.state == StateOver {
			p.Send(proto.Envelope("game_over", 0, proto.GameOverMsg{
				Winner: r.engine.Winner, Reason: r.overReason,
			}))
		}
	}
	return true
}

func (r *Room) skillListsLocked() (black, white []int) {
	for _, sk := range game.SideSkills(&r.engine.Black) {
		black = append(black, sk.ID)
	}
	for _, sk := range game.SideSkills(&r.engine.White) {
		white = append(white, sk.ID)
	}
	return
}

// ---------- timer ----------

func (r *Room) startTimerLocked() {
	if r.ticker != nil {
		return
	}
	done := make(chan struct{})
	r.done = done
	t := time.NewTicker(500 * time.Millisecond)
	r.ticker = t
	// Pass t/done as arguments so the goroutine never reads those fields
	// without holding the lock (a restart can overwrite them).
	go r.tickLoop(t, done)
}

func (r *Room) stopTimerLocked() {
	if r.ticker == nil {
		return
	}
	r.ticker.Stop()
	close(r.done)
	r.ticker = nil
}

func (r *Room) tickLoop(t *time.Ticker, done chan struct{}) {
	for {
		select {
		case <-done:
			return
		case <-t.C:
			r.tick()
		}
	}
}

func (r *Room) tick() {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.state != StatePlaying {
		return
	}
	now := r.now()

	// disconnect grace: an offline player loses 30s after disconnect.
	for _, m := range r.members {
		if !m.online() && m.offlineAt > 0 && now-m.offlineAt >= GraceMs {
			w := 1 - m.color
			r.abandoned = true
			r.finishLocked(&w, "left", nil)
			return
		}
	}

	// turn clock warning (sent once while <= 10s remain)
	remainMs := r.engine.TurnEndsAtMs - now
	if !r.warnSent && remainMs <= WarnAtSec*1000 && remainMs > 0 {
		r.warnSent = true
		remain := int((remainMs + 999) / 1000)
		msg := proto.Envelope("turn_expire_warn", 0, proto.TurnExpireWarnMsg{RemainSeconds: remain})
		for _, x := range r.members {
			if x.online() {
				x.peer.Send(msg)
			}
		}
	}
	if now >= r.engine.TurnEndsAtMs {
		if w, reason := r.engine.Timeout(r.engine.Turn); reason != "" {
			r.finishLocked(w, reason, nil)
		}
	}
}

// GrantForTest tops both sides up to energy and clears all skill cooldowns
// (test helper so integration scenarios can reach expensive skills quickly).
func (r *Room) GrantForTest(energy int) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.engine == nil {
		return
	}
	for c := 0; c < 2; c++ {
		s := r.engine.Side(c)
		s.Energy = energy
		for _, sk := range game.SideSkills(s) {
			sk.CD = 0
		}
	}
}

// timerRunning reports whether the tick goroutine is active (test helper).
func (r *Room) timerRunning() bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.ticker != nil
}
