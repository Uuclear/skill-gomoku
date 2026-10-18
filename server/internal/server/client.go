package server

import (
	"sync"
	"time"

	"github.com/gorilla/websocket"

	"chess-server/internal/proto"
)

const (
	writeWait      = 10 * time.Second
	pongWait       = 70 * time.Second
	pingPeriod     = 60 * time.Second
	sendBufferSize = 64
)

// Client is one websocket connection. It implements room.Peer; Send only
// enqueues while a dedicated write pump drains frames.
type Client struct {
	hub  *Hub
	conn *websocket.Conn
	send chan proto.Message

	mu       sync.RWMutex
	id       string
	nickname string
	closed   bool
	closeCh  chan struct{}
}

func newClient(hub *Hub, conn *websocket.Conn) *Client {
	return &Client{
		hub:     hub,
		conn:    conn,
		send:    make(chan proto.Message, sendBufferSize),
		closeCh: make(chan struct{}),
	}
}

// ID implements room.Peer (device id).
func (c *Client) ID() string {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.id
}

// Nickname implements room.Peer.
func (c *Client) Nickname() string {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.nickname
}

func (c *Client) identify(id, nickname string) {
	c.mu.Lock()
	c.id = id
	c.nickname = nickname
	c.mu.Unlock()
}

func (c *Client) identified() bool {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.id != ""
}

// Send enqueues a message. It never blocks: a stalled receiver is dropped.
func (c *Client) Send(m proto.Message) {
	c.mu.RLock()
	closed := c.closed
	c.mu.RUnlock()
	if closed {
		return
	}
	select {
	case c.send <- m:
	default:
		// slow consumer: tear the connection down
		c.forceClose()
	}
}

func (c *Client) forceClose() {
	c.mu.Lock()
	if c.closed {
		c.mu.Unlock()
		return
	}
	c.closed = true
	close(c.closeCh)
	c.mu.Unlock()
	_ = c.conn.Close()
}

func (c *Client) run() {
	go c.writePump()
	c.readPump()
	// read loop ended: detach / leave room and unregister
	if c.identified() {
		c.hub.manager.Disconnected(c)
	}
	c.hub.removeClient(c)
	c.forceClose()
}

func (c *Client) readPump() {
	c.conn.SetReadLimit(1 << 16)
	_ = c.conn.SetReadDeadline(time.Now().Add(pongWait))
	c.conn.SetPongHandler(func(string) error {
		return c.conn.SetReadDeadline(time.Now().Add(pongWait))
	})
	for {
		_, data, err := c.conn.ReadMessage()
		if err != nil {
			return
		}
		var msg proto.Message
		if err := jsonUnmarshal(data, &msg); err != nil {
			c.sendError("bad_message", "malformed envelope", 0)
			continue
		}
		c.hub.dispatch(c, msg)
	}
}

func (c *Client) writePump() {
	ticker := time.NewTicker(pingPeriod)
	defer ticker.Stop()
	for {
		select {
		case <-c.closeCh:
			return
		case msg, ok := <-c.send:
			if !ok {
				return
			}
			_ = c.conn.SetWriteDeadline(time.Now().Add(writeWait))
			if err := c.conn.WriteJSON(msg); err != nil {
				return
			}
		case <-ticker.C:
			_ = c.conn.SetWriteDeadline(time.Now().Add(writeWait))
			if err := c.conn.WriteMessage(websocket.PingMessage, nil); err != nil {
				return
			}
		}
	}
}

func (c *Client) sendError(code, message string, seq int64) {
	c.Send(proto.Envelope("error", seq, proto.ErrorMsg{Code: code, Message: message, Seq: seq}))
}
