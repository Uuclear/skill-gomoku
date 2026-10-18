package game

import "chess-server/internal/proto"

// Config holds the tunable game constants (fixed in this version).
type Config struct {
	StartEnergy int
	Gain        int
	ExtraGain   int // bonus per focused turn
	MaxEnergy   int
	TurnSeconds int
}

func DefaultConfig() Config {
	return Config{StartEnergy: 3, Gain: 2, ExtraGain: 2, MaxEnergy: 10, TurnSeconds: 60}
}

// SkillRuntime is one equipped skill's runtime state.
type SkillRuntime struct {
	ID       int
	CD       int // remaining cooldown in turns (0 = ready)
	UsesLeft int // -1 = unlimited; e.g. fortify limited to 1
}

// SideState is the per-player mutable state.
type SideState struct {
	Energy        int
	Skills        [4]*SkillRuntime
	SilenceTurns  int  // remaining own turns silenced
	NoEnergyTurns int  // remaining own turns without energy regen
	DiscountNext  bool // next skill costs -3 energy
	FocusTurns    int  // remaining own turns with extra energy regen
	ScoutTurns    int  // 侦查: see enemy energy/CDs (private view), own turns
	MinesRevealed bool // 扫雷: enemy mines stay visible
	// last stone placed normally by this side (for undo/disarm/cage)
	LastStoneX, LastStoneY int
	HasLastStone           bool
}

// PointMark is a board-attached marker with an expiry ply (and optional owner).
type PointMark struct {
	X, Y       int
	ExpirePly  int // ply at/after which it is cleaned at turn start; 0 for permanent
	Owner      int // color owning the marker (fortify/shield = own; seal = caster)
	Permanent  bool
}

type ZoneMark struct {
	CX, CY     int
	ExpirePly  int
	Owner      int // caster color: only the opponent is kept out of the zone
}

type CageMark struct {
	CX, CY     int
	ExpirePly  int
}

// PhantomMark tracks a phantom with its owner and expiry.
type PhantomMark struct {
	X, Y       int
	Color      int
	ExpirePly  int
}

// boardSnap is a historical board/markers capture for 时间回溯.
type boardSnap struct {
	board        *Board
	fortifies    []PointMark
	shields      []PointMark
	seals        []PointMark
	zones        []ZoneMark
	cage         *CageMark
	phantoms     []PhantomMark
	lastBlackX   int
	lastBlackY   int
	blackHasLast bool
	lastWhiteX   int
	lastWhiteY   int
	whiteHasLast bool
}

// Engine is the authoritative game state machine. All methods are expected to
// be called under the Room's serialization (one goroutine per game action).
type Engine struct {
	Cfg Config

	Board    *Board
	Status   string // playing / over
	Turn     int    // 0 black 1 white
	MoveCount int    // number of completed plies (normal stones placed / mines planted)
	Winner   *int   // nil while playing/draw

	Black SideState
	White SideState

	Fortifies []PointMark
	Shields   []PointMark
	Seals     []PointMark
	Zones     []ZoneMark
	Cage      *CageMark
	Phantoms  []PhantomMark

	// flags for the current ply
	SkipNextSide  int  // opponent skips when this side's ply finishes; -1 = none
	DoublePending bool // current side may place a second stone this ply
	DoubleFirst   bool // currently resolving the first (non-winning) half
	MineArmedX    int  // mine just triggered on this ply (-1 none)
	MineArmedY    int
	MineTriggered bool

	TurnEndsAtMs int64
	nextStoneID  int
	history      []boardSnap // index aligned: history[p] = state before ply p
}

func sideSkills(s *SideState) []*SkillRuntime {
	out := make([]*SkillRuntime, 0, 4)
	for _, sk := range s.Skills {
		if sk != nil {
			out = append(out, sk)
		}
	}
	return out
}

func (e *Engine) side(c int) *SideState {
	if c == Black {
		return &e.Black
	}
	return &e.White
}

// snapshotBoard captures physical board + board markers (rewind unit).
func (e *Engine) snapshotBoard() boardSnap {
	s := boardSnap{
		board:        e.Board.Clone(),
		fortifies:    cloneMarks(e.Fortifies),
		shields:      cloneMarks(e.Shields),
		seals:        cloneMarks(e.Seals),
		zones:        append([]ZoneMark(nil), e.Zones...),
		phantoms:     append([]PhantomMark(nil), e.Phantoms...),
		lastBlackX:   e.Black.LastStoneX,
		lastBlackY:   e.Black.LastStoneY,
		blackHasLast: e.Black.HasLastStone,
		lastWhiteX:   e.White.LastStoneX,
		lastWhiteY:   e.White.LastStoneY,
		whiteHasLast: e.White.HasLastStone,
	}
	if e.Cage != nil {
		c := *e.Cage
		s.cage = &c
	}
	return s
}

func cloneMarks(in []PointMark) []PointMark {
	out := make([]PointMark, len(in))
	copy(out, in)
	return out
}

// NewEngine creates a game with the two sides' chosen 4 skills.
func NewEngine(blackSkillIDs, whiteSkillIDs []int, nowMs int64) *Engine {
	e := &Engine{
		Cfg:         DefaultConfig(),
		Board:       NewBoard(),
		Status:      "playing",
		Turn:        Black,
		SkipNextSide: -1,
		MineArmedX:  -1,
		MineArmedY:  -1,
		nextStoneID: 1,
		TurnEndsAtMs: nowMs + int64(DefaultConfig().TurnSeconds)*1000,
	}
	initSide := func(ids []int) SideState {
		s := SideState{Energy: e.Cfg.StartEnergy, LastStoneX: -1, LastStoneY: -1}
		for i, id := range ids {
			if i >= 4 {
				break
			}
			uses := -1
			if id == 8 { // 坚守 limited once per game
				uses = 1
			}
			s.Skills[i] = &SkillRuntime{ID: id, CD: 0, UsesLeft: uses}
		}
		return s
	}
	e.Black = initSide(blackSkillIDs)
	e.White = initSide(whiteSkillIDs)
	// initial board capture (before ply 0)
	e.history = []boardSnap{e.snapshotBoard()}
	return e
}

func (e *Engine) pushHistory() {
	e.history = append(e.history, e.snapshotBoard())
	// rewind may target 6 plies back, requiring the snapshot from MoveCount-6;
	// with history[0]=state before ply 0 that is up to 8 entries.
	if len(e.history) > 8 {
		e.history = e.history[len(e.history)-8:]
	}
}

// PlantMine plants a hidden mine on an empty point and ends the ply (the mine
// skill replaces the normal placement). Legality is checked by the executor.
func (e *Engine) PlantMine(color, x, y int, nowMs int64) *ActionResult {
	e.Board.PutMine(x, y, color)
	e.MoveCount++
	res := &ActionResult{Events: []proto.GameEvent{{Kind: "mine_plant", Actor: color, X: x, Y: y}}}
	e.afterPlacementAdvance(color, nowMs)
	return res
}

// Rewind restores board/markers to 6 plies (3 full rounds) ago. Energy and
// cooldowns are intentionally NOT reverted. The acting side keeps its turn.
func (e *Engine) Rewind(color int) error {
	target := e.MoveCount - 6
	if target < 0 {
		return ErrInvalidAction
	}
	// history index layout: history[i] = state before ply i, but old entries
	// are trimmed; locate by MoveCount delta.
	idx := -1
	for i := range e.history {
		// entry i corresponds to ply (MoveCount - (len(history)-1) + i)
		ply := e.MoveCount - (len(e.history) - 1) + i
		if ply == target {
			idx = i
			break
		}
	}
	if idx < 0 {
		return ErrInvalidAction
	}
	snap := e.history[idx]
	e.Board = snap.board.Clone()
	e.Fortifies = cloneMarks(snap.fortifies)
	e.Shields = cloneMarks(snap.shields)
	e.Seals = cloneMarks(snap.seals)
	e.Zones = append([]ZoneMark(nil), snap.zones...)
	e.Phantoms = append([]PhantomMark(nil), snap.phantoms...)
	e.Black.LastStoneX, e.Black.LastStoneY, e.Black.HasLastStone = snap.lastBlackX, snap.lastBlackY, snap.blackHasLast
	e.White.LastStoneX, e.White.LastStoneY, e.White.HasLastStone = snap.lastWhiteX, snap.lastWhiteY, snap.whiteHasLast
	if snap.cage != nil {
		c := *snap.cage
		e.Cage = &c
	} else {
		e.Cage = nil
	}
	e.MoveCount = target
	return nil
}

// ---- snapshot construction ----

func marksToDTO(in []PointMark) []proto.MarkPointDTO {
	if len(in) == 0 {
		return []proto.MarkPointDTO{}
	}
	out := make([]proto.MarkPointDTO, 0, len(in))
	for _, m := range in {
		out = append(out, proto.MarkPointDTO{X: m.X, Y: m.Y, ExpireAtMove: m.ExpirePly})
	}
	return out
}

func sideDTO(s *SideState) proto.SideStateDTO {
	d := proto.SideStateDTO{
		Energy:        s.Energy,
		SilenceTurns:  s.SilenceTurns,
		NoEnergyTurns: s.NoEnergyTurns,
		DiscountNext:  s.DiscountNext,
		FocusTurns:    s.FocusTurns,
		Skills:        []proto.SkillStateDTO{},
	}
	for _, sk := range sideSkills(s) {
		d.Skills = append(d.Skills, proto.SkillStateDTO{ID: sk.ID, CD: sk.CD, UsesLeft: sk.UsesLeft})
	}
	return d
}
