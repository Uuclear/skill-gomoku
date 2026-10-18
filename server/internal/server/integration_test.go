package server

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"

	"chess-server/internal/proto"
)

// wsClient is a scripted test peer: one goroutine pumps incoming frames into
// a buffered channel so the test can assert on typed messages.
type wsClient struct {
	t    *testing.T
	conn *websocket.Conn
	recv chan proto.Message
	done chan struct{}
}

func dialClient(t *testing.T, server *httptest.Server) *wsClient {
	t.Helper()
	url := "ws" + strings.TrimPrefix(server.URL, "http") + "/ws"
	conn, _, err := websocket.DefaultDialer.Dial(url, nil)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	c := &wsClient{t: t, conn: conn, recv: make(chan proto.Message, 128), done: make(chan struct{})}
	go func() {
		defer close(c.done)
		for {
			_, data, err := conn.ReadMessage()
			if err != nil {
				return
			}
			var m proto.Message
			if err := json.Unmarshal(data, &m); err != nil {
				t.Errorf("unmarshal frame: %v", err)
				return
			}
			select {
			case c.recv <- m:
			default:
				t.Errorf("test recv buffer full (type=%s)", m.Type)
			}
		}
	}()
	return c
}

func (c *wsClient) send(typ string, v any) {
	raw, _ := json.Marshal(v)
	if err := c.conn.WriteJSON(proto.Message{Type: typ, Data: raw}); err != nil {
		c.t.Fatalf("send %s: %v", typ, err)
	}
}

func (c *wsClient) closeNow() {
	_ = c.conn.Close()
	<-c.done
	// let the server observe the close and run Disconnected
	time.Sleep(100 * time.Millisecond)
}

// waitFor returns the next message of the given type(s), skipping others.
func (c *wsClient) waitFor(timeout time.Duration, types ...string) proto.Message {
	deadline := time.After(timeout)
	want := map[string]bool{}
	for _, ty := range types {
		want[ty] = true
	}
	for {
		select {
		case m := <-c.recv:
			if want[m.Type] {
				return m
			}
		case <-deadline:
			c.t.Fatalf("timeout waiting for %v", types)
		}
	}
}

// pumpEvents consumes exactly n game_event frames, skipping any other frame
// types (e.g. emotes, warns). Every public event is broadcast to BOTH peers,
// so the test must drain each side to keep its receive queue deterministic.
func (c *wsClient) pumpEvents(n int) []proto.GameEventMsg {
	out := make([]proto.GameEventMsg, 0, n)
	for len(out) < n {
		m := c.waitFor(2*time.Second, "game_event")
		out = append(out, mustData[proto.GameEventMsg](c.t, m))
	}
	return out
}

func mustData[T any](t *testing.T, m proto.Message) T {
	t.Helper()
	var v T
	if err := json.Unmarshal(m.Data, &v); err != nil {
		t.Fatalf("unmarshal %s data: %v", m.Type, err)
	}
	return v
}

func newTestServer() (*httptest.Server, *Hub) {
	hub := NewHub()
	srv := httptest.NewServer(http.HandlerFunc(hub.ServeWS))
	return srv, hub
}

// blackLoadout / whiteLoadout together cover the scenario skills:
// 3 双落, 4 炸弹, 5 地雷, 18 沉默 | 29 追加回合, 30 时间回溯
var blackLoadout = []int{3, 4, 5, 18}
var whiteLoadout = []int{29, 30, 25, 11}

// TestFullMatchFriendRoom drives two scripted clients through the whole flow:
// create/join room -> ready -> start -> 双落/炸弹/追加回合/地雷/沉默/时间回溯
// -> 断线重连 -> 投降判负 -> 再来一局.
func TestFullMatchFriendRoom(t *testing.T) {
	srv, hub := newTestServer()
	defer srv.Close()
	defer hub.Close()

	black := dialClient(t, srv)
	white := dialClient(t, srv)
	defer black.conn.Close()
	defer white.conn.Close()

	// ---- login ----
	black.send("login", proto.LoginReq{Nickname: "小黑", DeviceID: "dev-black"})
	white.send("login", proto.LoginReq{Nickname: "小白", DeviceID: "dev-white"})
	black.waitFor(time.Second, "login_ok")
	white.waitFor(time.Second, "login_ok")

	// ---- friend room: host creates, guest joins, guest readies, host starts ----
	black.send("room_create", proto.SkillIDsReq{SkillIDs: blackLoadout})
	rs := mustData[proto.RoomStateMsg](t, black.waitFor(time.Second, "room_state"))
	if len(rs.Players) != 1 || rs.RoomCode == "" {
		t.Fatalf("bad initial room state: %+v", rs)
	}
	code := rs.RoomCode

	white.send("room_join", proto.RoomJoinReq{RoomCode: code, SkillIDs: whiteLoadout})
	mustData[proto.RoomStateMsg](t, white.waitFor(time.Second, "room_state"))
	mustData[proto.RoomStateMsg](t, black.waitFor(time.Second, "room_state"))

	white.send("ready", struct{}{})
	readyState := mustData[proto.RoomStateMsg](t, black.waitFor(time.Second, "room_state"))
	if !readyState.Players[1].Ready {
		t.Fatalf("guest not ready: %+v", readyState.Players)
	}

	black.send("start", struct{}{})
	gsBlack := mustData[proto.GameStartMsg](t, black.waitFor(2*time.Second, "game_start"))
	gsWhite := mustData[proto.GameStartMsg](t, white.waitFor(2*time.Second, "game_start"))
	if gsBlack.YourColor != 0 || gsWhite.YourColor != 1 {
		t.Fatalf("colors wrong: black=%d white=%d", gsBlack.YourColor, gsWhite.YourColor)
	}
	if gsBlack.BoardSize != 15 {
		t.Fatalf("board size = %d", gsBlack.BoardSize)
	}

	// both sides receive the first turn_start snapshot (black to move)
	snapBlack := mustData[proto.GameEventMsg](t, black.waitFor(time.Second, "game_event"))
	snapWhite := mustData[proto.GameEventMsg](t, white.waitFor(time.Second, "game_event"))
	if snapBlack.Event.Kind != "turn_start" || snapBlack.State.Turn != 0 {
		t.Fatalf("bad first snapshot black: kind=%s turn=%d", snapBlack.Event.Kind, snapBlack.State.Turn)
	}
	// opponent energy is masked without 侦查
	if snapWhite.State.Black.Energy != -1 {
		t.Fatalf("opponent energy should be masked, got %d", snapWhite.State.Black.Energy)
	}
	if snapBlack.State.Black.Energy != 3 {
		t.Fatalf("own energy = %d want 3", snapBlack.State.Black.Energy)
	}

	room := hub.Manager().RoomForPlayer("dev-black")
	if room == nil || !room.HasMember("dev-white") {
		t.Fatalf("room index missing: %+v", room)
	}

	// doMove performs a placement and drains the single broadcast frame from
	// BOTH peers, returning the actor's event message.
	doMove := func(actor, other *wsClient, x, y int) proto.GameEventMsg {
		actor.send("move", proto.MoveReq{X: x, Y: y})
		res := actor.pumpEvents(1)[0]
		other.pumpEvents(1)
		return res
	}
	// doSkill performs a successful skill cast with the expected number of
	// public events; both peers drain that many frames.
	doSkill := func(caster, other *wsClient, id int, targets []proto.Point, events int) []proto.GameEventMsg {
		caster.send("use_skill", proto.UseSkillReq{SkillID: id, Targets: targets})
		got := caster.pumpEvents(events)
		other.pumpEvents(events)
		return got
	}

	// ---- 6 opening plies, spaced so nobody accidentally wins ----
	opening := []proto.Point{
		{X: 0, Y: 0}, {X: 1, Y: 0}, {X: 2, Y: 0},
		{X: 3, Y: 0}, {X: 4, Y: 0}, {X: 5, Y: 0},
	}
	players := [2]*wsClient{black, white}
	for i, p := range opening {
		ev := doMove(players[i%2], players[(i+1)%2], p.X, p.Y)
		if ev.State.MoveCount != i+1 {
			t.Fatalf("opening ply %d moveCount=%d", i, ev.State.MoveCount)
		}
	}

	// ---- 双落 (3): black acts twice in one ply ----
	room.GrantForTest(10)
	sk := doSkill(black, white, 3, nil, 1)[0]
	if sk.Event.Kind != "skill" || sk.Event.SkillID != 3 {
		t.Fatalf("want double skill event, got %+v", sk.Event)
	}
	// skill does not pass the turn: first half stays with black
	d1 := doMove(black, white, 0, 2)
	if d1.Event.Kind != "double_first" || d1.State.Turn != 0 {
		t.Fatalf("double_first wrong: kind=%s turn=%d", d1.Event.Kind, d1.State.Turn)
	}
	d2 := doMove(black, white, 1, 2)
	if d2.Event.Kind != "double_second" || d2.State.Turn != 1 {
		t.Fatalf("double_second wrong: kind=%s turn=%d", d2.Event.Kind, d2.State.Turn)
	}

	// ---- 追加回合 (29): white skips black's next turn and acts twice ----
	room.GrantForTest(10)
	doSkill(white, black, 29, nil, 1)
	e1 := doMove(white, black, 0, 3) // white move #1, black's turn is skipped
	if e1.State.Turn != 1 {
		t.Fatalf("extra turn: white should remain actor, turn=%d", e1.State.Turn)
	}
	e2 := doMove(white, black, 1, 3) // white move #2
	if e2.State.Turn != 0 {
		t.Fatalf("after extra turn consumed, black should act, turn=%d", e2.State.Turn)
	}

	// ---- 地雷 (5): black plants; white triggers it by placing on the mine ----
	room.GrantForTest(10)
	mine := proto.Point{X: 7, Y: 7}
	// asymmetric broadcast: caster gets skill+mine_plant (2), opponent gets
	// only the masked generic skill event (mine_plant is hidden).
	black.send("use_skill", proto.UseSkillReq{SkillID: 5, Targets: []proto.Point{mine}})
	plantEv := black.pumpEvents(2)
	if plantEv[0].Event.Kind != "skill" || plantEv[0].Event.SkillID != 5 {
		t.Fatalf("mine skill event: %+v", plantEv[0].Event)
	}
	if plantEv[1].Event.Kind != "mine_plant" || plantEv[1].Event.X != 7 || plantEv[1].Event.Y != 7 {
		t.Fatalf("mine_plant for owner: %+v", plantEv[1].Event)
	}
	foundMine := false
	for _, p := range plantEv[1].State.MinePositions {
		if p == mine {
			foundMine = true
		}
	}
	if !foundMine {
		t.Fatalf("own mine missing from caster snapshot: %+v", plantEv[1].State.MinePositions)
	}
	// opponent receives only one frame (mine_plant hidden) with coordinates
	// stripped from the generic skill event, and never sees the mine.
	leak := white.pumpEvents(1)[0]
	if leak.Event.Kind != "skill" || leak.Event.SkillID != 5 {
		t.Fatalf("opponent mine frame kind: %+v", leak.Event)
	}
	if leak.Event.X != 0 || leak.Event.Y != 0 || len(leak.Event.Points) != 0 {
		t.Fatalf("mine coords leaked to opponent: %+v", leak.Event)
	}
	for _, p := range leak.State.MinePositions {
		if p == mine {
			t.Fatalf("enemy mine leaked into opponent snapshot")
		}
	}
	// planting mines ends the caster's turn: white steps onto it and is destroyed
	trig := doMove(white, black, mine.X, mine.Y)
	if trig.Event.Kind != "mine_trigger" || trig.Event.X != mine.X || trig.Event.Y != mine.Y {
		t.Fatalf("mine_trigger wrong: %+v", trig.Event)
	}
	for _, s := range trig.State.Stones {
		if s.X == mine.X && s.Y == mine.Y {
			t.Fatalf("stone should be destroyed by mine")
		}
	}
	if trig.State.Turn != 0 {
		t.Fatalf("after mine trigger black should act, turn=%d", trig.State.Turn)
	}

	// ---- 炸弹 (4): black removes white's opening stone at (1,0) ----
	room.GrantForTest(10)
	bombEv := doSkill(black, white, 4, []proto.Point{{X: 1, Y: 0}}, 2)
	if bombEv[0].Event.Kind != "skill" {
		t.Fatalf("want skill event, got %s", bombEv[0].Event.Kind)
	}
	if bombEv[1].Event.Kind != "bomb" || bombEv[1].Event.X != 1 || bombEv[1].Event.Y != 0 {
		t.Fatalf("bomb event wrong: %+v", bombEv[1].Event)
	}
	for _, s := range bombEv[1].State.Stones {
		if s.X == 1 && s.Y == 0 {
			t.Fatalf("bombed stone still present")
		}
	}

	// ---- 沉默 (18): black silences white; white's skill on its next turn is
	// refused, but white can still place stones ----
	room.GrantForTest(10)
	doSkill(black, white, 18, nil, 1)
	// skills don't pass the turn; black places to hand over to silenced white.
	doMove(black, white, 10, 10)
	// white starts its turn silenced: skill 11 is rejected.
	white.send("use_skill", proto.UseSkillReq{SkillID: 11})
	errMsg := mustData[proto.ErrorMsg](t, white.waitFor(time.Second, "error"))
	if errMsg.Code != "silenced" {
		t.Fatalf("want silenced error, got %s", errMsg.Code)
	}
	// a silenced player can still place stones; that ply ends the silence tick
	sm := doMove(white, black, 8, 8)
	if sm.State.Turn != 0 {
		t.Fatalf("after white silenced move, black should act: turn=%d", sm.State.Turn)
	}

	// ---- 时间回溯 (30): after enough plies, white rewinds 6 half-moves ----
	beforeEv := doMove(black, white, 9, 9) // hand turn back to white
	beforeMC := beforeEv.State.MoveCount
	if beforeMC < 6 {
		t.Fatalf("need >=6 plies for rewind, got %d", beforeMC)
	}
	room.GrantForTest(10)
	rw := doSkill(white, black, 30, nil, 2)
	if rw[0].Event.Kind != "skill" || rw[0].Event.SkillID != 30 {
		t.Fatalf("rewind skill event: %+v", rw[0].Event)
	}
	if rw[1].Event.Kind != "rewind" {
		t.Fatalf("rewind event: %+v", rw[1].Event)
	}
	if rw[1].State.MoveCount >= beforeMC {
		t.Fatalf("rewind did not move back: before=%d after=%d",
			beforeMC, rw[1].State.MoveCount)
	}

	// ---- emotes work mid-game (broadcast to both seats) ----
	black.send("emote", proto.EmoteReq{ID: 3})
	em := mustData[proto.EmoteMsg](t, white.waitFor(time.Second, "emote"))
	if em.FromColor != 0 || em.ID != 3 {
		t.Fatalf("emote wrong: %+v", em)
	}

	// ---- disconnect / reconnect mid-game ----
	white.closeNow()
	ol := mustData[proto.OpponentLeftMsg](t, black.waitFor(2*time.Second, "opponent_left"))
	if ol.Reason != "disconnect" {
		t.Fatalf("opponent_left reason = %q want disconnect", ol.Reason)
	}
	// same device logs back in: session is reattached and state is replayed
	white2 := dialClient(t, srv)
	defer white2.conn.Close()
	white2.send("login", proto.LoginReq{Nickname: "小白", DeviceID: "dev-white"})
	mustData[proto.LoginOkMsg](t, white2.waitFor(time.Second, "login_ok"))
	mustData[proto.GameStartMsg](t, white2.waitFor(2*time.Second, "game_start"))
	rcSnap := mustData[proto.GameEventMsg](t, white2.waitFor(time.Second, "game_event"))
	if rcSnap.Event.Kind != "turn_start" {
		t.Fatalf("reconnect snapshot: %s", rcSnap.Event.Kind)
	}

	// ---- surrender decides the game ----
	white2.send("surrender", struct{}{})
	overB := mustData[proto.GameOverMsg](t, black.waitFor(2*time.Second, "game_over"))
	overW := mustData[proto.GameOverMsg](t, white2.waitFor(2*time.Second, "game_over"))
	if overB.Reason != "surrender" || overB.Winner == nil || *overB.Winner != 0 {
		t.Fatalf("black game_over wrong: %+v", overB)
	}
	if overW.Winner == nil || *overW.Winner != 0 {
		t.Fatalf("white game_over wrong: %+v", overW)
	}

	// ---- rematch: both accept -> fresh game ----
	black.send("rematch", proto.RematchReq{Accept: true})
	mustData[proto.RematchRequestedMsg](t, white2.waitFor(2*time.Second, "rematch_requested"))
	white2.send("rematch", proto.RematchReq{Accept: true})
	r1 := mustData[proto.GameStartMsg](t, black.waitFor(2*time.Second, "game_start"))
	r2 := mustData[proto.GameStartMsg](t, white2.waitFor(2*time.Second, "game_start"))
	if r1.YourColor != 0 || r2.YourColor != 1 {
		t.Fatalf("rematch colors wrong: %d %d", r1.YourColor, r2.YourColor)
	}
	fresh := mustData[proto.GameEventMsg](t, black.waitFor(time.Second, "game_event"))
	mustData[proto.GameEventMsg](t, white2.waitFor(time.Second, "game_event"))
	if fresh.State.MoveCount != 0 || len(fresh.State.Stones) != 0 || fresh.State.Turn != 0 {
		t.Fatalf("rematch board not fresh: mc=%d stones=%d turn=%d",
			fresh.State.MoveCount, len(fresh.State.Stones), fresh.State.Turn)
	}
}

// TestQuickMatch verifies the FIFO pairing path: first client waits, the second
// pairs them and a match room auto-starts with black/white assigned in order.
func TestQuickMatch(t *testing.T) {
	srv, hub := newTestServer()
	defer srv.Close()
	defer hub.Close()

	a := dialClient(t, srv)
	b := dialClient(t, srv)
	defer a.conn.Close()
	defer b.conn.Close()

	a.send("login", proto.LoginReq{DeviceID: "qa"})
	b.send("login", proto.LoginReq{DeviceID: "qb"})
	a.waitFor(time.Second, "login_ok")
	b.waitFor(time.Second, "login_ok")

	a.send("match_queue", proto.SkillIDsReq{SkillIDs: blackLoadout})
	mustData[proto.MatchWaitingMsg](t, a.waitFor(time.Second, "match_waiting"))

	b.send("match_queue", proto.SkillIDsReq{SkillIDs: whiteLoadout})
	mfA := mustData[proto.MatchFoundMsg](t, a.waitFor(2*time.Second, "match_found"))
	mfB := mustData[proto.MatchFoundMsg](t, b.waitFor(2*time.Second, "match_found"))
	if mfA.Color != 0 || mfB.Color != 1 {
		t.Fatalf("quick match colors: a=%d b=%d", mfA.Color, mfB.Color)
	}
	if mfA.RoomID != mfB.RoomID || mfA.RoomID == "" {
		t.Fatalf("room ids mismatch: %q vs %q", mfA.RoomID, mfB.RoomID)
	}
	// auto start: game_start then the first turn_start snapshot on each seat
	mustData[proto.GameStartMsg](t, a.waitFor(2*time.Second, "game_start"))
	mustData[proto.GameStartMsg](t, b.waitFor(2*time.Second, "game_start"))
	mustData[proto.GameEventMsg](t, a.waitFor(time.Second, "game_event"))
	mustData[proto.GameEventMsg](t, b.waitFor(time.Second, "game_event"))
}
