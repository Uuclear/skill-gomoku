package game

import (
	"errors"

	"chess-server/internal/proto"
)

// ActionResult bundles events produced by one request. The Room converts these
// into protocol messages; State is always the latest snapshot.
type ActionResult struct {
	Events  []proto.GameEvent
	Private []PrivateResult // per-color private payloads (e.g. oracle/threat)
	Over    bool
	Winner  *int
	Reason  string
	WinLine []proto.Point
}

// PrivateResult is a skill_private message destined to exactly one side.
type PrivateResult struct {
	Color   int
	SkillID int
	Payload any
}

var (
	ErrNotYourTurn   = errors.New("not your turn")
	ErrIllegalPoint  = errors.New("illegal point")
	ErrPointBlocked  = errors.New("point is occupied or sealed")
	ErrGameOver      = errors.New("game already over")
	ErrInvalidAction = errors.New("invalid action")
)

// Snapshot builds the authoritative snapshot. viewerColor selects the private
// view: own mines are always visible; enemy mines only after 扫雷.
func (e *Engine) Snapshot(viewerColor int) proto.Snapshot {
	s := proto.Snapshot{
		Status:       e.Status,
		Turn:         e.Turn,
		MoveCount:    e.MoveCount,
		TurnEndsAtMs: e.TurnEndsAtMs,
		Stones:       []proto.StoneDTO{},
		Phantoms:     []proto.PhantomDTO{},
		Fortifies:    marksToDTO(e.Fortifies),
		Shields:      marksToDTO(e.Shields),
		Seals:        marksToDTO(e.Seals),
		Black:        sideDTO(&e.Black),
		White:        sideDTO(&e.White),
	}
	for _, r := range e.Board.AllStones() {
		s.Stones = append(s.Stones, proto.StoneDTO{X: r.X, Y: r.Y, C: r.Color})
	}
	for _, p := range e.Phantoms {
		s.Phantoms = append(s.Phantoms, proto.PhantomDTO{X: p.X, Y: p.Y, C: p.Color, ExpireAtMove: p.ExpirePly})
	}
	for _, z := range e.Zones {
		s.Zones = append(s.Zones, proto.ZoneDTO{CX: z.CX, CY: z.CY, ExpireAtMove: z.ExpirePly})
	}
	if e.Cage != nil {
		s.Cage = &proto.CageDTO{Center: proto.Point{X: e.Cage.CX, Y: e.Cage.CY}, ExpireAtMove: e.Cage.ExpirePly}
	}
	// mines: own always visible, enemy only after that viewer's 扫雷.
	viewer := e.side(viewerColor)
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			owner := e.Board.MineAt(x, y)
			if owner == -1 {
				continue
			}
			if owner == viewerColor {
				s.MinePositions = append(s.MinePositions, proto.Point{X: x, Y: y})
			} else if viewer.MinesRevealed {
				s.MinePositions = append(s.MinePositions, proto.Point{X: x, Y: y})
			}
		}
	}
	return s
}

// ---- placement legality ----

func inZone(x, y, cx, cy int) bool {
	dx, dy := x-cx, y-cy
	if dx < 0 {
		dx = -dx
	}
	if dy < 0 {
		dy = -dy
	}
	return dx <= 1 && dy <= 1
}

func in5x5(x, y, cx, cy int) bool {
	dx, dy := x-cx, y-cy
	if dx < 0 {
		dx = -dx
	}
	if dy < 0 {
		dy = -dy
	}
	return dx <= 2 && dy <= 2
}

// legalPlacement checks bounds, occupancy and active markers for color c.
// An enemy mine on the point is allowed through so ApplyMove can trigger it;
// the point is otherwise blocked by stones, phantoms or the side's own mine.
func (e *Engine) legalPlacement(x, y, c int) error {
	if !InBounds(x, y) {
		return ErrIllegalPoint
	}
	cell := &e.Board.Cells[x][y]
	if cell.StoneColor != empty || cell.Phantom != empty || cell.MineOwner == c {
		return ErrPointBlocked
	}
	for _, m := range e.Seals {
		if m.Owner != c && m.X == x && m.Y == y && m.ExpirePly > e.MoveCount {
			return ErrPointBlocked
		}
	}
	for _, z := range e.Zones {
		if z.Owner != c && inZone(x, y, z.CX, z.CY) && z.ExpirePly > e.MoveCount {
			// 禁区: the caster's opponent cannot place inside the 3x3.
			return ErrPointBlocked
		}
	}
	if e.Cage != nil && e.Cage.ExpirePly > e.MoveCount {
		if !in5x5(x, y, e.Cage.CX, e.Cage.CY) {
			return ErrPointBlocked
		}
	}
	return nil
}

// ApplyMove resolves a normal placement (or the second stone of 双落).
// Returns events up to and including win/draw. The Room broadcasts the
// snapshot carried implicitly via Engine.Snapshot.
func (e *Engine) ApplyMove(color, x, y int, nowMs int64) (*ActionResult, error) {
	if e.Status != "playing" {
		return nil, ErrGameOver
	}
	if color != e.Turn {
		return nil, ErrNotYourTurn
	}
	if err := e.legalPlacement(x, y, color); err != nil {
		return nil, err
	}

	res := &ActionResult{}
	kind := "move"
	if e.DoublePending {
		if e.DoubleFirst {
			kind = "double_first"
		} else {
			kind = "double_second"
		}
	}

	// mine trigger BEFORE placement
	if mine := e.Board.MineAt(x, y); mine != -1 && mine != color {
		e.Board.RemoveMine(x, y)
		res.Events = append(res.Events, proto.GameEvent{Kind: "mine_trigger", Actor: mine, X: x, Y: y})
		// incoming stone is destroyed: no stone is placed, but the ply counts.
		e.MoveCount++
		e.DoublePending = false
		e.DoubleFirst = false
		e.afterPlacementAdvance(color, nowMs)
		return res, nil
	}

	// place
	stoneID := e.nextStoneID
	e.nextStoneID++
	e.Board.PlaceStone(x, y, color, stoneID)
	s := e.side(color)
	s.LastStoneX, s.LastStoneY, s.HasLastStone = x, y, true
	e.MoveCount++
	mainEvent := proto.GameEvent{Kind: kind, Actor: color, X: x, Y: y}

	// win check is skipped for the FIRST stone of 双落.
	win := false
	if !(e.DoublePending && e.DoubleFirst) {
		if wc, line, ok := e.Board.CheckWinAt(x, y); ok {
			win = true
			res.Winner = &wc
			res.Reason = "five"
			res.WinLine = line
		}
	}
	res.Events = append(res.Events, mainEvent)

	if win {
		e.finish(*res.Winner, "five")
		res.Over = true
		return res, nil
	}
	if e.Board.CountStones() >= BoardSize*BoardSize {
		draw := (*int)(nil)
		e.finishDraw()
		res.Over = true
		res.Winner = draw
		res.Reason = "full"
		return res, nil
	}

	// 双落: first half keeps the same player's turn.
	if e.DoublePending && e.DoubleFirst {
		e.DoubleFirst = false
		e.TurnEndsAtMs = nowMs + int64(e.Cfg.TurnSeconds)*1000
		return res, nil
	}
	e.DoublePending = false
	e.DoubleFirst = false

	e.afterPlacementAdvance(color, nowMs)
	return res, nil
}

// afterPlacementAdvance handles end-of-ply cleanup, possible skip (追加回合),
// and start-of-next-ply settlement.
func (e *Engine) afterPlacementAdvance(lastColor int, nowMs int64) {
	e.cleanExpired()
	if e.SkipNextSide == opp(lastColor) {
		e.SkipNextSide = -1
		// same color acts again: settle start for them immediately
		e.Turn = lastColor
		e.startPly(lastColor)
	} else {
		e.Turn = opp(lastColor)
		e.startPly(e.Turn)
	}
	e.TurnEndsAtMs = nowMs + int64(e.Cfg.TurnSeconds)*1000
	e.pushHistory()
}

// startPly performs turn-start settlement for the acting color. Duration
// effects apply on the current turn first and are decremented afterwards, so
// a value of N affects exactly N of the side's own turns.
func (e *Engine) startPly(c int) {
	s := e.side(c)
	// energy regen (suppressed while NoEnergyTurns is active)
	if s.NoEnergyTurns > 0 {
		s.NoEnergyTurns--
	} else {
		gain := e.Cfg.Gain
		if s.FocusTurns > 0 {
			gain += e.Cfg.ExtraGain
		}
		s.Energy = minMax(s.Energy+gain, 0, e.Cfg.MaxEnergy)
	}
	// skill cooldowns tick
	for _, sk := range sideSkills(s) {
		if sk.CD > 0 {
			sk.CD--
		}
	}
	// phantoms owned by this side expire on their 2nd own turn
	kept := e.Phantoms[:0]
	for _, p := range e.Phantoms {
		if p.Color == c && e.MoveCount >= p.ExpirePly {
			e.Board.RemovePhantom(p.X, p.Y)
			continue
		}
		kept = append(kept, p)
	}
	e.Phantoms = kept
	// duration counters are consumed by this turn (apply-then-decrement)
	if s.SilenceTurns > 0 {
		s.SilenceTurns--
	}
	if s.FocusTurns > 0 {
		s.FocusTurns--
	}
	if s.ScoutTurns > 0 {
		s.ScoutTurns--
	}
}

// cleanExpired removes seals/zones/cage/shields whose expiry ply is reached.
func (e *Engine) cleanExpired() {
	filter := func(marks []PointMark) []PointMark {
		out := marks[:0]
		for _, m := range marks {
			if m.Permanent || m.ExpirePly > e.MoveCount {
				out = append(out, m)
			}
		}
		return out
	}
	e.Shields = filter(e.Shields)
	e.Seals = filter(e.Seals)
	if e.Cage != nil && e.Cage.ExpirePly <= e.MoveCount {
		e.Cage = nil
	}
	keptZ := e.Zones[:0]
	for _, z := range e.Zones {
		if z.ExpirePly > e.MoveCount {
			keptZ = append(keptZ, z)
		}
	}
	e.Zones = keptZ
}

func (e *Engine) finish(w int, reason string) {
	e.Status = "over"
	e.Winner = &w
}

func (e *Engine) finishDraw() {
	e.Status = "over"
	e.Winner = nil
}

func opp(c int) int { return 1 - c }

func minMax(v, lo, hi int) int {
	if v < lo {
		return lo
	}
	if v > hi {
		return hi
	}
	return v
}

// Surrender ends the game in the opponent's favor.
func (e *Engine) Surrender(color int) *int {
	w := opp(color)
	e.finish(w, "surrender")
	return &w
}

// Timeout ends the game if the timed-out side is the current actor.
func (e *Engine) Timeout(color int) (*int, string) {
	if e.Status != "playing" || color != e.Turn {
		return nil, ""
	}
	w := opp(color)
	e.finish(w, "timeout")
	return &w, "timeout"
}
