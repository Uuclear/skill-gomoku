// Package proto defines the WebSocket JSON wire protocol between the Unity
// client and the Go server. These structs are the single source of truth for
// the message envelope, request/response DTOs and game state snapshots.
package proto

import "encoding/json"

// Message is the JSON text-frame envelope.
type Message struct {
	Type string          `json:"type"`
	Seq  int64           `json:"seq,omitempty"`
	Data json.RawMessage `json:"data,omitempty"`
}

// Point is a board coordinate, x/y in [0,14].
type Point struct {
	X int `json:"x"`
	Y int `json:"y"`
}

// ---------- Client -> Server ----------

type LoginReq struct {
	Nickname string `json:"nickname"`
	DeviceID string `json:"deviceId"`
}

type SkillIDsReq struct {
	SkillIDs []int `json:"skillIds"`
}

type RoomJoinReq struct {
	RoomCode string `json:"roomCode"`
	SkillIDs []int  `json:"skillIds"`
}

type MoveReq struct {
	X int `json:"x"`
	Y int `json:"y"`
}

type UseSkillReq struct {
	SkillID int     `json:"skillId"`
	Targets []Point `json:"targets"`
	Dir     int     `json:"dir"` // 0:+x 1:+y 2:-x 3:-y
}

type RematchReq struct {
	Accept bool `json:"accept"`
}

type EmoteReq struct {
	ID int `json:"id"`
}

// ---------- Server -> Client ----------

type LoginOkMsg struct {
	PlayerID string `json:"playerId"`
}

type ErrorMsg struct {
	Code    string `json:"code"`
	Message string `json:"message"`
	Seq     int64  `json:"seq,omitempty"`
}

type OpponentInfo struct {
	ID       string `json:"id"`
	Nickname string `json:"nickname"`
}

type MatchFoundMsg struct {
	RoomID    string        `json:"roomId"`
	Color     int           `json:"color"` // 0 black 1 white
	Opponent  OpponentInfo  `json:"opponent"`
	SkillIDs  []int         `json:"skillIds"`
}

type RoomPlayerInfo struct {
	ID       string `json:"id"`
	Nickname string `json:"nickname"`
	Ready    bool   `json:"ready"`
	SkillCnt int    `json:"skillCnt"`
	IsHost   bool   `json:"isHost"`
}

type RoomStateMsg struct {
	RoomCode  string            `json:"roomCode"`
	HostID    string            `json:"hostId"`
	Players   []RoomPlayerInfo  `json:"players"`
}

type GameConfig struct {
	StartEnergy int `json:"startEnergy"`
	Gain        int `json:"gain"`
	MaxEnergy   int `json:"maxEnergy"`
	TurnSeconds int `json:"turnSeconds"`
	BoardSize   int `json:"boardSize"`
}

type GameStartMsg struct {
	RoomID       string     `json:"roomId"`
	BoardSize    int        `json:"boardSize"`
	YourColor    int        `json:"yourColor"`
	BlackSkills  []int      `json:"blackSkills"`
	WhiteSkills  []int      `json:"whiteSkills"`
	Config       GameConfig `json:"config"`
}

type SkillStateDTO struct {
	ID       int `json:"id"`
	CD       int `json:"cd"`
	UsesLeft int `json:"usesLeft"` // -1 = unlimited
}

type SideStateDTO struct {
	Energy        int             `json:"energy"`
	Skills        []SkillStateDTO `json:"skills"`
	SilenceTurns  int             `json:"silenceTurns"`
	NoEnergyTurns int             `json:"noEnergyTurns"`
	DiscountNext  bool            `json:"discountNext"`
	FocusTurns    int             `json:"focusTurns"`
}

type StoneDTO struct {
	X int `json:"x"`
	Y int `json:"y"`
	C int `json:"c"` // 0 black 1 white
}

type PhantomDTO struct {
	X            int `json:"x"`
	Y            int `json:"y"`
	C            int `json:"c"`
	ExpireAtMove int `json:"expireAtMove"`
}

type MarkPointDTO struct {
	X            int `json:"x"`
	Y            int `json:"y"`
	ExpireAtMove int `json:"expireAtMove,omitempty"`
}

type ZoneDTO struct {
	CX           int `json:"cx"`
	CY           int `json:"cy"`
	ExpireAtMove int `json:"expireAtMove"`
}

type CageDTO struct {
	Center       Point `json:"center"`
	ExpireAtMove int   `json:"expireAtMove"`
}

// Snapshot is the full authoritative game state. MinePositions is filled only
// for the private view (own mines + revealed enemy mines).
type Snapshot struct {
	Status        string         `json:"status"` // playing/over
	Turn          int            `json:"turn"`   // 0 black 1 white
	MoveCount     int            `json:"moveCount"`
	TurnEndsAtMs  int64          `json:"turnEndsAtMs"`
	Stones        []StoneDTO     `json:"stones"`
	Phantoms      []PhantomDTO   `json:"phantoms"`
	Fortifies     []MarkPointDTO `json:"fortifies"`
	Shields       []MarkPointDTO `json:"shields"`
	Seals         []MarkPointDTO `json:"seals"`
	Zones         []ZoneDTO      `json:"zones"`
	Cage          *CageDTO       `json:"cage,omitempty"`
	Black         SideStateDTO   `json:"black"`
	White         SideStateDTO   `json:"white"`
	MinePositions []Point        `json:"minePositions,omitempty"`
}

// GameEvent is delivered together with a Snapshot after every state change.
type GameEvent struct {
	Kind      string  `json:"kind"` // move/skill/mine_trigger/phantom_expire/turn_skip/turn_start/rewind/double_second/...
	Actor     int     `json:"actor"`
	SkillID   int     `json:"skillId,omitempty"`
	X         int     `json:"x,omitempty"`
	Y         int     `json:"y,omitempty"`
	Points    []Point `json:"points,omitempty"`
	ExtraTurn bool    `json:"extraTurn,omitempty"`
}

type GameEventMsg struct {
	Event GameEvent `json:"event"`
	State Snapshot  `json:"state"`
}

type PrivateSkillMsg struct {
	SkillID int             `json:"skillId"`
	Payload json.RawMessage `json:"payload"`
}

type OraclePayload struct {
	Points []Point `json:"points"`
}

type AnalyzePayload struct {
	Black LineStats `json:"black"`
	White LineStats `json:"white"`
}

type LineStats struct {
	Longest int `json:"longest"`
	Open3   int `json:"open3"`
	Open4   int `json:"open4"`
}

type GameOverMsg struct {
	Winner  *int    `json:"winner"` // nil = draw
	Reason  string  `json:"reason"` // five/surrender/timeout/full/left
	WinLine []Point `json:"winLine,omitempty"`
}

type EmoteMsg struct {
	FromColor int `json:"fromColor"`
	ID        int `json:"id"`
}

type MatchWaitingMsg struct {
	WaitSeconds int `json:"waitSeconds"`
}

type RematchRequestedMsg struct {
	FromColor int `json:"fromColor"`
}

type GameRestartMsg struct {
	BoardSize   int        `json:"boardSize"`
	YourColor   int        `json:"yourColor"`
	BlackSkills []int      `json:"blackSkills"`
	WhiteSkills []int      `json:"whiteSkills"`
	Config      GameConfig `json:"config"`
}

type OpponentLeftMsg struct {
	Reason string `json:"reason"` // left/disconnect
}

type TurnExpireWarnMsg struct {
	RemainSeconds int `json:"remainSeconds"`
}

type ReconnectMsg struct {
	Kind    string     `json:"kind"` // room/game
	Room    *RoomStateMsg `json:"room,omitempty"`
	Start   *GameStartMsg  `json:"start,omitempty"`
}

// Envelope builds a wire Message from a typed payload.
func Envelope(typ string, seq int64, v any) Message {
	raw, _ := json.Marshal(v)
	m := Message{Type: typ, Data: raw}
	if seq != 0 {
		m.Seq = seq
	}
	return m
}
