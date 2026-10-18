package server

import (
	"encoding/json"
	"errors"

	"chess-server/internal/game"
	"chess-server/internal/proto"
	"chess-server/internal/room"
	"chess-server/internal/skill"
)

func jsonUnmarshal(data []byte, v any) error { return json.Unmarshal(data, v) }

// dispatch routes one decoded envelope.
func (h *Hub) dispatch(c *Client, msg proto.Message) {
	switch msg.Type {
	case "login":
		h.handleLogin(c, msg.Data, msg.Seq)
	case "match_queue":
		h.handleMatchQueue(c, msg.Data)
	case "match_cancel":
		requireLogin(c, msg.Seq, func() { h.manager.CancelMatch(c) })
	case "room_create":
		h.handleRoomCreate(c, msg.Data)
	case "room_join":
		h.handleRoomJoin(c, msg.Data)
	case "ready":
		requireLogin(c, msg.Seq, func() { h.mapError(c, msg.Seq, h.manager.SetReady(c)) })
	case "start":
		requireLogin(c, msg.Seq, func() {
			if r := h.manager.RoomForPlayer(c.ID()); r != nil {
				h.mapError(c, msg.Seq, r.Start(c))
			} else {
				c.sendError(code(room.ErrNotInRoom), "not in a room", msg.Seq)
			}
		})
	case "move":
		h.handleMove(c, msg.Data, msg.Seq)
	case "use_skill":
		h.handleUseSkill(c, msg.Data, msg.Seq)
	case "surrender":
		requireLogin(c, msg.Seq, func() {
			if r := h.manager.RoomForPlayer(c.ID()); r != nil {
				h.mapError(c, msg.Seq, r.Surrender(c.ID()))
			}
		})
	case "emote":
		var req proto.EmoteReq
		if unmarshal(c, msg.Data, &req, msg.Seq) {
			if r := h.manager.RoomForPlayer(c.ID()); r != nil {
				h.mapError(c, msg.Seq, r.Emote(c.ID(), req.ID))
			}
		}
	case "rematch":
		var req proto.RematchReq
		if unmarshal(c, msg.Data, &req, msg.Seq) {
			if r := h.manager.RoomForPlayer(c.ID()); r != nil {
				h.mapError(c, msg.Seq, r.RematchVote(c.ID(), req.Accept))
			}
		}
	case "ping":
		c.Send(proto.Envelope("pong", msg.Seq, struct{}{}))
	default:
		c.sendError("unknown_type", "unknown message type", msg.Seq)
	}
}

func requireLogin(c *Client, seq int64, fn func()) {
	if !c.identified() {
		c.sendError("not_logged_in", "login required", seq)
		return
	}
	fn()
}

func unmarshal(c *Client, data json.RawMessage, v any, seq int64) bool {
	if err := json.Unmarshal(data, v); err != nil {
		c.sendError("bad_message", "malformed payload", seq)
		return false
	}
	return true
}

func (h *Hub) handleLogin(c *Client, data json.RawMessage, seq int64) {
	var req proto.LoginReq
	if !unmarshal(c, data, &req, seq) {
		return
	}
	if req.DeviceID == "" {
		c.sendError("bad_request", "deviceId required", seq)
		return
	}
	nick := req.Nickname
	if nick == "" {
		nick = "棋手" + req.DeviceID
	}
	if len(nick) > 12 {
		nick = nick[:12]
	}
	c.identify(req.DeviceID, nick)
	h.replaceClient(c)
	c.Send(proto.Envelope("login_ok", seq, proto.LoginOkMsg{PlayerID: req.DeviceID}))
	// session recovery: if the device still owns a seat, reattach and replay.
	h.manager.Attach(c)
}

func (h *Hub) handleMatchQueue(c *Client, data json.RawMessage) {
	var req proto.SkillIDsReq
	if !unmarshal(c, data, &req, 0) {
		return
	}
	if !c.identified() {
		c.sendError("not_logged_in", "login required", 0)
		return
	}
	h.mapError(c, 0, h.manager.EnqueueMatch(c, req.SkillIDs))
}

func (h *Hub) handleRoomCreate(c *Client, data json.RawMessage) {
	var req proto.SkillIDsReq
	if !unmarshal(c, data, &req, 0) {
		return
	}
	if !c.identified() {
		c.sendError("not_logged_in", "login required", 0)
		return
	}
	_, err := h.manager.CreateFriendRoom(c, req.SkillIDs)
	h.mapError(c, 0, err)
}

func (h *Hub) handleRoomJoin(c *Client, data json.RawMessage) {
	var req proto.RoomJoinReq
	if !unmarshal(c, data, &req, 0) {
		return
	}
	if !c.identified() {
		c.sendError("not_logged_in", "login required", 0)
		return
	}
	h.mapError(c, 0, h.manager.JoinFriendRoom(c, req.RoomCode, req.SkillIDs))
}

func (h *Hub) handleMove(c *Client, data json.RawMessage, seq int64) {
	var req proto.MoveReq
	if !unmarshal(c, data, &req, seq) {
		return
	}
	if !c.identified() {
		c.sendError("not_logged_in", "login required", seq)
		return
	}
	if r := h.manager.RoomForPlayer(c.ID()); r != nil {
		h.mapError(c, seq, r.Move(c.ID(), req.X, req.Y))
	}
}

func (h *Hub) handleUseSkill(c *Client, data json.RawMessage, seq int64) {
	var req proto.UseSkillReq
	if !unmarshal(c, data, &req, seq) {
		return
	}
	if !c.identified() {
		c.sendError("not_logged_in", "login required", seq)
		return
	}
	if r := h.manager.RoomForPlayer(c.ID()); r != nil {
		h.mapError(c, seq, r.UseSkill(c.ID(), req.SkillID, req.Targets, req.Dir))
	}
}

// code maps a domain error to a stable wire error code.
func code(err error) string {
	switch {
	case err == nil:
		return ""
	case errors.Is(err, room.ErrBadLoadout):
		return "bad_loadout"
	case errors.Is(err, room.ErrRoomCode):
		return "room_not_found"
	case errors.Is(err, room.ErrRoomFull):
		return "room_full"
	case errors.Is(err, room.ErrNotHost):
		return "not_host"
	case errors.Is(err, room.ErrNotReady):
		return "not_ready"
	case errors.Is(err, room.ErrAlreadyOver):
		return "bad_state"
	case errors.Is(err, room.ErrAlreadyInGame):
		return "already_busy"
	case errors.Is(err, room.ErrNotInRoom):
		return "not_in_room"
	case errors.Is(err, game.ErrNotYourTurn):
		return "not_your_turn"
	case errors.Is(err, game.ErrIllegalPoint):
		return "illegal_point"
	case errors.Is(err, game.ErrPointBlocked):
		return "point_blocked"
	case errors.Is(err, game.ErrGameOver):
		return "game_over"
	case errors.Is(err, game.ErrInvalidAction):
		return "invalid_action"
	case errors.Is(err, skill.ErrNotFound):
		return "skill_unknown"
	case errors.Is(err, skill.ErrNotEquipped):
		return "skill_not_equipped"
	case errors.Is(err, skill.ErrSilenced):
		return "silenced"
	case errors.Is(err, skill.ErrOnCooldown):
		return "on_cooldown"
	case errors.Is(err, skill.ErrNoUses):
		return "no_uses"
	case errors.Is(err, skill.ErrNoEnergy):
		return "no_energy"
	case errors.Is(err, skill.ErrBadTarget):
		return "bad_target"
	case errors.Is(err, skill.ErrTooEarly):
		return "too_early"
	case errors.Is(err, skill.ErrNotUsable):
		return "not_usable"
	default:
		return "bad_request"
	}
}

func (h *Hub) mapError(c *Client, seq int64, err error) {
	if err == nil {
		return
	}
	c.sendError(code(err), err.Error(), seq)
}
