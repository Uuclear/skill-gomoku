package room

import (
	"crypto/rand"
	"sync"
	"time"

	"chess-server/internal/proto"
)

// staleAfter is how long a finished/empty room lingers for reconnects
// before the background reaper removes it.
const staleAfter = 2 * time.Minute

// queuedPlayer is one FIFO quick-match entry.
type queuedPlayer struct {
	peer   Peer
	skills []int
}

// Manager owns the room index, friend-room codes, the player->room reverse
// index (reconnect lookup) and the FIFO match queue.
type Manager struct {
	mu         sync.Mutex
	rooms      map[string]*Room
	codes      map[string]string // room code -> room id
	playerRoom map[string]string // player id -> room id
	queue      []queuedPlayer
	closeOnce  sync.Once
	stopCh     chan struct{}
}

// NewManager constructs the manager and starts the stale-room reaper.
func NewManager() *Manager {
	m := &Manager{
		rooms:      map[string]*Room{},
		codes:      map[string]string{},
		playerRoom: map[string]string{},
		stopCh:     make(chan struct{}),
	}
	go m.reapLoop()
	return m
}

// Close stops background goroutines.
func (m *Manager) Close() {
	m.closeOnce.Do(func() { close(m.stopCh) })
}

func (m *Manager) newRoomLocked(kind, code string) *Room {
	id := genID(12)
	if code == "" {
		for {
			code = genCode()
			if _, taken := m.codes[code]; !taken {
				break
			}
		}
	}
	r := &Room{
		ID:        id,
		Code:      code,
		Kind:      kind,
		state:     StateWaiting,
		members:   map[string]*Member{},
		rematch:   map[string]bool{},
		now:       nowMs,
		createdAt: nowMs(),
		manager:   m,
	}
	m.rooms[id] = r
	m.codes[code] = id
	return r
}

// CreateFriendRoom seats the host in a new waiting room and returns its code.
func (m *Manager) CreateFriendRoom(p Peer, skills []int) (string, error) {
	if !validLoadout(skills) {
		return "", ErrBadLoadout
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	if _, busy := m.playerRoom[p.ID()]; busy {
		return "", ErrAlreadyInGame
	}
	r := m.newRoomLocked(KindFriend, "")
	r.mu.Lock()
	r.addHostLocked(p, skills)
	state := r.roomStateLocked()
	r.mu.Unlock()
	m.playerRoom[p.ID()] = r.ID
	p.Send(proto.Envelope("room_state", 0, state))
	return r.Code, nil
}

// JoinFriendRoom seats a guest by room code.
func (m *Manager) JoinFriendRoom(p Peer, code string, skills []int) error {
	if !validLoadout(skills) {
		return ErrBadLoadout
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	if _, busy := m.playerRoom[p.ID()]; busy {
		return ErrAlreadyInGame
	}
	id, ok := m.codes[code]
	if !ok {
		return ErrRoomCode
	}
	r := m.rooms[id]
	if err := r.joinLocked(p, skills); err != nil {
		return err
	}
	m.playerRoom[p.ID()] = r.ID
	return nil
}

// SetReady marks a lobby member ready.
func (m *Manager) SetReady(p Peer) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	id, ok := m.playerRoom[p.ID()]
	if !ok {
		return ErrNotInRoom
	}
	return m.rooms[id].setReadyLocked(p.ID())
}

// EnqueueMatch puts a player into the FIFO quick-match queue. If someone is
// already waiting the two are paired into a match room that auto-starts.
func (m *Manager) EnqueueMatch(p Peer, skills []int) error {
	if !validLoadout(skills) {
		return ErrBadLoadout
	}
	m.mu.Lock()
	defer m.mu.Unlock()
	if _, busy := m.playerRoom[p.ID()]; busy {
		return ErrAlreadyInGame
	}
	for _, q := range m.queue {
		if q.peer.ID() == p.ID() {
			return ErrAlreadyInGame
		}
	}

	// No opponent yet: enqueue and confirm.
	if len(m.queue) == 0 {
		m.queue = append(m.queue, queuedPlayer{peer: p, skills: append([]int(nil), skills...)})
		p.Send(proto.Envelope("match_waiting", 0, proto.MatchWaitingMsg{}))
		return nil
	}

	// Pair with the head of the queue.
	head := m.queue[0]
	m.queue = m.queue[1:]

	r := m.newRoomLocked(KindMatch, "")
	r.mu.Lock()
	r.addHostLocked(head.peer, head.skills)
	r.addGuestLocked(p, skills)
	m.playerRoom[head.peer.ID()] = r.ID
	m.playerRoom[p.ID()] = r.ID

	notify := func(peer Peer, color int, opp Peer, ownSkills []int) {
		peer.Send(proto.Envelope("match_found", 0, proto.MatchFoundMsg{
			RoomID:   r.ID,
			Color:    color,
			Opponent: proto.OpponentInfo{ID: opp.ID(), Nickname: opp.Nickname()},
			SkillIDs: append([]int(nil), ownSkills...),
		}))
	}
	notify(head.peer, 0, p, head.skills)
	notify(p, 1, head.peer, skills)

	r.startLocked()
	r.mu.Unlock()
	return nil
}

// CancelMatch removes a player from the quick-match queue.
func (m *Manager) CancelMatch(p Peer) {
	m.mu.Lock()
	defer m.mu.Unlock()
	for i, q := range m.queue {
		if q.peer.ID() == p.ID() {
			m.queue = append(m.queue[:i], m.queue[i+1:]...)
			return
		}
	}
}

// RoomForPlayer returns the room a player belongs to (nil if none).
func (m *Manager) RoomForPlayer(playerID string) *Room {
	m.mu.Lock()
	defer m.mu.Unlock()
	if id, ok := m.playerRoom[playerID]; ok {
		return m.rooms[id]
	}
	return nil
}

// Attach rebinds a reconnecting peer to its room, if any.
func (m *Manager) Attach(p Peer) bool {
	m.mu.Lock()
	defer m.mu.Unlock()
	id, ok := m.playerRoom[p.ID()]
	if !ok {
		return false
	}
	r, ok := m.rooms[id]
	if !ok {
		return false
	}
	return r.Attach(p)
}

// Disconnected handles a websocket close: mid-game players stay seated
// (Detach, 30s grace); lobby/queue players are removed immediately.
func (m *Manager) Disconnected(p Peer) {
	m.mu.Lock()
	id, inRoom := m.playerRoom[p.ID()]
	r := (*Room)(nil)
	if inRoom {
		r = m.rooms[id]
	}
	// drop from match queue if present
	for i, q := range m.queue {
		if q.peer.ID() == p.ID() {
			m.queue = append(m.queue[:i], m.queue[i+1:]...)
			break
		}
	}
	if r == nil {
		m.mu.Unlock()
		return
	}

	r.mu.Lock()
	state := r.state
	hostLeft := state == StateWaiting && r.hostID == p.ID()
	var empty bool
	if state == StatePlaying || state == StateOver {
		r.mu.Unlock()
		m.mu.Unlock()
		r.Detach(p.ID())
		return
	}
	// waiting friend room
	if hostLeft {
		r.mu.Unlock()
		m.dropRoomLocked(r)
		m.mu.Unlock()
		return
	}
	empty = r.removeMemberLocked(p.ID(), true)
	r.mu.Unlock()
	delete(m.playerRoom, p.ID())
	if empty {
		m.dropRoomLocked(r)
	}
	m.mu.Unlock()
}

// dropRoomLocked removes a room and its indexes. Caller holds Manager.mu and
// must NOT hold the room lock.
func (m *Manager) dropRoomLocked(r *Room) {
	delete(m.rooms, r.ID)
	delete(m.codes, r.Code)
	r.mu.Lock()
	for id := range r.members {
		delete(m.playerRoom, id)
	}
	r.stopTimerLocked()
	r.mu.Unlock()
}

// reapLoop periodically discards rooms long past their useful life.
func (m *Manager) reapLoop() {
	ticker := time.NewTicker(30 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-m.stopCh:
			return
		case <-ticker.C:
			m.reap()
		}
	}
}

func (m *Manager) reap() {
	now := nowMs()
	m.mu.Lock()
	defer m.mu.Unlock()
	for _, r := range m.rooms {
		r.mu.Lock()
		state := r.state
		empty := len(r.members) == 0
		allOffline := !empty
		for _, mem := range r.members {
			if mem.online() {
				allOffline = false
			}
		}
		endedAt := r.endedAt
		r.mu.Unlock()

		staleFinished := (state == StateOver && endedAt > 0 && now-endedAt > staleAfter.Milliseconds())
		staleEmpty := empty && state != StatePlaying
		staleOfflineWaiting := allOffline && state == StateWaiting
		if staleFinished || staleEmpty || staleOfflineWaiting {
			m.dropRoomLocked(r)
		}
	}
}

// ---------- id generation ----------

const codeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"

func genCode() string {
	b := make([]byte, 5)
	rand.Read(b)
	for i := range b {
		b[i] = codeAlphabet[int(b[i])%len(codeAlphabet)]
	}
	return string(b)
}

const idAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"

func genID(n int) string {
	b := make([]byte, n)
	rand.Read(b)
	for i := range b {
		b[i] = idAlphabet[int(b[i])%len(idAlphabet)]
	}
	return string(b)
}
