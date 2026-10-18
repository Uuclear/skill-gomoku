package game

import "chess-server/internal/proto"

// LineSummary describes one color's current board shape.
type LineSummary struct {
	Longest int
	Open3   int
	Open4   int
}

var analyzeDirs = [4][2]int{{1, 0}, {0, 1}, {1, 1}, {1, -1}}

// AnalyzeLines counts, for each color, the longest consecutive run (phantoms
// participate for their owner) and open-three / open-four shapes. Each
// maximal run along a direction is counted once.
func AnalyzeLines(b *Board) (black, white LineSummary) {
	seen := [2]map[[4]int]bool{{}, {}}
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			for d := 0; d < 4; d++ {
				c := b.LineColorAt(x, y)
				if c == empty {
					continue
				}
				dx, dy := analyzeDirs[d][0], analyzeDirs[d][1]
				// only start of a maximal run
				if b.LineColorAt(x-dx, y-dy) == c {
					continue
				}
				n := 1
				for b.LineColorAt(x+dx*n, y+dy*n) == c {
					n++
				}
				key := [4]int{x, y, d, n}
				if seen[c][key] {
					continue
				}
				seen[c][key] = true
				sum := &black
				if c == White {
					sum = &white
				}
				if n > sum.Longest {
					sum.Longest = n
				}
				openBefore := openAt(b, x-dx, y-dy, c)
				openAfter := openAt(b, x+dx*n, y+dy*n, c)
				if n == 3 && openBefore && openAfter {
					sum.Open3++
				}
				if n == 4 && openBefore && openAfter {
					sum.Open4++
				}
			}
		}
	}
	return
}

// openAt reports whether (x,y) is an in-bounds empty intersection usable as a
// continuation point for color c (a phantom of the other color blocks too).
func openAt(b *Board, x, y, c int) bool {
	if !InBounds(x, y) {
		return false
	}
	return b.LineColorAt(x, y) == empty
}

// ImmediateWinPoints returns every empty point where placing one stone of
// color c completes five or more. Phantoms of c count as c stones.
func ImmediateWinPoints(b *Board, c int) []proto.Point {
	out := []proto.Point{}
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			if b.StoneColorAt(x, y) != empty || b.Cells[x][y].Phantom != empty {
				continue
			}
			// candidate: does c get 5 through (x,y) in any direction?
			for _, d := range analyzeDirs {
				n := 1
				for i := 1; i < 5; i++ {
					if b.LineColorAt(x+d[0]*i, y+d[1]*i) != c {
						break
					}
					n++
				}
				for i := 1; i < 5; i++ {
					if b.LineColorAt(x-d[0]*i, y-d[1]*i) != c {
						break
					}
					n++
				}
				if n >= 5 {
					out = append(out, proto.Point{X: x, Y: y})
					break
				}
			}
		}
	}
	return out
}

// HintPoints is the server-side 神谕: simple attack/defense score per empty
// point. Returns up to 3 highest-scoring points.
func HintPoints(b *Board, c int) []proto.Point {
	opp := 1 - c
	type scored struct {
		p     proto.Point
		score int
	}
	best := []scored{}
	for x := 0; x < BoardSize; x++ {
		for y := 0; y < BoardSize; y++ {
			if b.StoneColorAt(x, y) != empty || b.Cells[x][y].Phantom != empty {
				continue
			}
			s := pointScore(b, x, y, c)*2 + pointScore(b, x, y, opp)
			// slight center bias keeps hints away from useless edges
			s -= (absInt(x-7) + absInt(y-7)) / 8
			if len(best) < 3 || s > best[len(best)-1].score {
				best = append(best, scored{proto.Point{X: x, Y: y}, s})
				// keep sorted desc, top 3
				for i := len(best) - 1; i > 0 && best[i].score > best[i-1].score; i-- {
					best[i], best[i-1] = best[i-1], best[i]
				}
				if len(best) > 3 {
					best = best[:3]
				}
			}
		}
	}
	out := make([]proto.Point, 0, len(best))
	for _, sc := range best {
		out = append(out, sc.p)
	}
	return out
}

// pointScore evaluates how placing color c at (x,y) shapes its lines.
func pointScore(b *Board, x, y, c int) int {
	total := 0
	for _, d := range analyzeDirs {
		n := 1
		openA, openB := false, false
		for i := 1; i < 5; i++ {
			if b.LineColorAt(x+d[0]*i, y+d[1]*i) != c {
				openA = InBounds(x+d[0]*i, y+d[1]*i) && b.LineColorAt(x+d[0]*i, y+d[1]*i) == empty
				break
			}
			n++
		}
		for i := 1; i < 5; i++ {
			if b.LineColorAt(x-d[0]*i, y-d[1]*i) != c {
				openB = InBounds(x-d[0]*i, y-d[1]*i) && b.LineColorAt(x-d[0]*i, y-d[1]*i) == empty
				break
			}
			n++
		}
		switch {
		case n >= 5:
			total += 100000
		case n == 4 && openA && openB:
			total += 10000
		case n == 4:
			total += 1000
		case n == 3 && openA && openB:
			total += 1000
		case n == 3:
			total += 100
		case n == 2:
			total += 10
		default:
			total += 1
		}
	}
	return total
}

func absInt(v int) int {
	if v < 0 {
		return -v
	}
	return v
}
