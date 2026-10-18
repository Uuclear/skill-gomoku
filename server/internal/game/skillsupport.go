package game

import "chess-server/internal/proto"

// Empty is the empty-cell color sentinel exported for skill targeting.
func Empty() int { return empty }

// Side returns the per-color mutable state.
func (e *Engine) Side(c int) *SideState { return e.side(c) }

// SkipNextSideForTest exposes the pending skip flag for tests.
func (e *Engine) SkipNextSideForTest() int { return e.SkipNextSide }

// SetEnergyForTest overrides both sides' energy (test helper).
func (e *Engine) SetEnergyForTest(v int) {
	e.Black.Energy = v
	e.White.Energy = v
}

// IsFortified reports whether the normal stone at (x,y) carries a permanent
// 坚守 mark (immune to all removal/move/swap effects).
func (e *Engine) IsFortified(x, y int) bool {
	for _, m := range e.Fortifies {
		if m.X == x && m.Y == y {
			return true
		}
	}
	return false
}

// IsShielded reports an active 护盾 on the point whose owner is protected
// against enemy-targeted effects.
func (e *Engine) IsShielded(x, y int) bool {
	for _, m := range e.Shields {
		if m.X == x && m.Y == y && m.ExpirePly > e.MoveCount {
			return true
		}
	}
	return false
}

// RemoveNormalStone removes a normal stone (never a phantom), returning false
// if there was no stone. Used by board-control skills.
func (e *Engine) RemoveNormalStone(x, y int) bool {
	if e.Board.StoneColorAt(x, y) == empty {
		return false
	}
	e.Board.RemoveStone(x, y)
	return true
}

// FindNormalStone returns the position of the stone with the given id.
func (e *Engine) FindNormalStone(id int) (proto.Point, bool) {
	for _, r := range e.Board.AllStones() {
		if r.ID == id {
			return proto.Point{X: r.X, Y: r.Y}, true
		}
	}
	return proto.Point{}, false
}

// HasEquipped reports whether the side equipped the given skill id.
func (e *Engine) HasEquipped(color, id int) bool {
	for _, sk := range sideSkills(e.side(color)) {
		if sk.ID == id {
			return true
		}
	}
	return false
}

// SideSkills exports the side's equipped skill runtime slots.
func SideSkills(s *SideState) []*SkillRuntime { return sideSkills(s) }

// SkillRuntime returns the side's runtime slot for the given skill id.
func (e *Engine) SkillRuntime(color, id int) *SkillRuntime {
	for _, sk := range sideSkills(e.side(color)) {
		if sk.ID == id {
			return sk
		}
	}
	return nil
}
