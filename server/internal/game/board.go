package game

import "chess-server/internal/proto"

// BoardSize is fixed 15x15 for this game.
const BoardSize = 15

// Black / White color indices.
const (
	Black = 0
	White = 1
)

const empty = -1

// Cell holds the physical board content at one intersection.
// A normal stone and a mine cannot coexist (mine occupies an empty point and
// blocks placement); phantom lives in its own slot because it never blocks a
// normal stone's placement (phantoms are only placed on empty cells and once
// present the cell is considered occupied for placement purposes).
type Cell struct {
	StoneColor int // -1 empty, 0 black, 1 white
	StoneID    int // unique id of normal stone (for undo/disarm tracking), -1 if none
	Phantom    int // -1 none, otherwise owner color
	MineOwner  int // -1 none, otherwise owner color
}

// Board is the 15x15 physical state.
type Board struct {
	Cells [BoardSize][BoardSize]Cell
}

// StoneRecord is an indexed normal stone on the board.
type StoneRecord struct {
	X, Y  int
	Color int
	ID    int
}

func NewBoard() *Board {
	b := &Board{}
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			b.Cells[x][y] = Cell{StoneColor: empty, StoneID: -1, Phantom: empty, MineOwner: empty}
		}
	}
	return b
}

func InBounds(x, y int) bool {
	return x >= 0 && x < BoardSize && y >= 0 && y < BoardSize
}

// StoneColorAt returns the normal stone color or -1.
func (b *Board) StoneColorAt(x, y int) int {
	if !InBounds(x, y) {
		return empty
	}
	return b.Cells[x][y].StoneColor
}

// LineColorAt returns the color that participates in five-in-a-row at the
// point: normal stone first, otherwise phantom. Empty returns -1.
func (b *Board) LineColorAt(x, y int) int {
	if !InBounds(x, y) {
		return empty
	}
	c := b.Cells[x][y]
	if c.StoneColor != empty {
		return c.StoneColor
	}
	return c.Phantom
}

// CanPlace reports whether a normal stone may be placed: in bounds and the
// point has neither normal stone, phantom nor mine.
func (b *Board) CanPlace(x, y int) bool {
	if !InBounds(x, y) {
		return false
	}
	c := b.Cells[x][y]
	return c.StoneColor == empty && c.Phantom == empty && c.MineOwner == empty
}

// PlaceStone puts a normal stone. Caller must check CanPlace.
func (b *Board) PlaceStone(x, y, color, id int) {
	b.Cells[x][y].StoneColor = color
	b.Cells[x][y].StoneID = id
}

func (b *Board) RemoveStone(x, y int) {
	b.Cells[x][y].StoneColor = empty
	b.Cells[x][y].StoneID = -1
}

func (b *Board) PutPhantom(x, y, color int) {
	b.Cells[x][y].Phantom = color
}

func (b *Board) RemovePhantom(x, y int) {
	b.Cells[x][y].Phantom = empty
}

func (b *Board) PutMine(x, y, owner int) {
	b.Cells[x][y].MineOwner = owner
}

func (b *Board) RemoveMine(x, y int) {
	b.Cells[x][y].MineOwner = empty
}

func (b *Board) MineAt(x, y int) int {
	if !InBounds(x, y) {
		return empty
	}
	return b.Cells[x][y].MineOwner
}

func (b *Board) StoneIDAt(x, y int) int {
	if !InBounds(x, y) {
		return -1
	}
	return b.Cells[x][y].StoneID
}

// SwapStones exchanges any two normal stones. Position legality is the
// caller's responsibility. Mines/phantoms stay in place.
func (b *Board) SwapStones(x1, y1, x2, y2 int) {
	c1, c2 := &b.Cells[x1][y1], &b.Cells[x2][y2]
	c1.StoneColor, c2.StoneColor = c2.StoneColor, c1.StoneColor
	c1.StoneID, c2.StoneID = c2.StoneID, c1.StoneID
}

// MoveStone moves a normal stone to an empty point.
func (b *Board) MoveStone(fx, fy, tx, ty int) {
	cf, ct := &b.Cells[fx][fy], &b.Cells[tx][ty]
	ct.StoneColor = cf.StoneColor
	ct.StoneID = cf.StoneID
	cf.StoneColor = empty
	cf.StoneID = -1
}

// AllStones enumerates normal stones.
func (b *Board) AllStones() []StoneRecord {
	var out []StoneRecord
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			if c := b.Cells[x][y]; c.StoneColor != empty {
				out = append(out, StoneRecord{X: x, Y: y, Color: c.StoneColor, ID: c.StoneID})
			}
		}
	}
	return out
}

func (b *Board) CountStones() int {
	n := 0
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			if b.Cells[x][y].StoneColor != empty {
				n++
			}
		}
	}
	return n
}

var fourDirs = [4][2]int{{1, 0}, {0, 1}, {1, 1}, {1, -1}}

// CheckWinAt checks whether the stone just placed at (x,y) gives its color
// five or more in a row (long chain wins). Returns the winning color, the full
// winning line and true. Phantoms participate via LineColorAt.
func (b *Board) CheckWinAt(x, y int) (int, []proto.Point, bool) {
	color := b.LineColorAt(x, y)
	if color == empty {
		return 0, nil, false
	}
	for _, d := range fourDirs {
		line := []proto.Point{{X: x, Y: y}}
		for i := 1; ; i++ {
			nx, ny := x+d[0]*i, y+d[1]*i
			if b.LineColorAt(nx, ny) != color {
				break
			}
			line = append(line, proto.Point{X: nx, Y: ny})
		}
		for i := 1; ; i++ {
			nx, ny := x-d[0]*i, y-d[1]*i
			if b.LineColorAt(nx, ny) != color {
				break
			}
			line = append([]proto.Point{{X: nx, Y: ny}}, line...)
		}
		if len(line) >= 5 {
			return color, line, true
		}
	}
	return 0, nil, false
}

// Clone deep-copies the board (used for rewind snapshots).
func (b *Board) Clone() *Board {
	nb := &Board{}
	nb.Cells = b.Cells // value copy of the 225-cell value array
	return nb
}
