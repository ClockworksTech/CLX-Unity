// Prints CLX datagrams as "name<TAB>hex" lines, encoded by the same msgpack
// library and with the same struct shapes as the Go senders: Clockworks Server
// (SeratoStreamer/datatypes/types.go, transmission.go) and prolink-clx
// (internal/clx/sender.go). Keep the structs field for field with those files.
package main

import (
	"crypto/md5"
	"crypto/sha256"
	"encoding/hex"
	"fmt"

	"github.com/vmihailenco/msgpack/v5"
)

const (
	title       = "Déjà Vu (Extended Mix)"
	chunkSize   = 1200
	pixelCount  = 2000
	sampleRate  = 44100
	samplesPerP = 124
)

type deck struct {
	Pitch              float64
	Position           float64
	Position2          float64
	NormalizedPosition float64
	BPM                float64
	Length             float64
	EQLow              float32
	EQMid              float32
	EQHigh             float32
	Deck               int
	Sequence           int `json:"Seq,omitempty"`
	Beat               int
}

type control struct {
	UpfaderA     float64
	UpfaderB     float64
	UpfaderC     float64
	UpfaderD     float64
	Crossfader   float64
	SeratoActive int
	AppState     string
}

type meta struct {
	Deck     int
	Title    string
	Artist   string
	Album    string
	FilePath string
}

type deckEvent struct {
	Event  string `json:"event"`
	Deck   uint8  `json:"deck"`
	Values []any  `json:"values"`
}

type binaryMessage struct {
	Type         string
	Hash         [32]byte
	Total        uint64
	Order        uint32
	TotalPackets uint32
	Data         []byte
}

type waveformData struct {
	Hash [32]byte
	Data []byte
}

type beatgridMarker struct {
	Bpm         float32
	Position    float32
	Terminal    bool
	BeatsToNext uint32
}

type beatgridPayload struct {
	Hash    [32]byte
	Total   uint32
	Markers []beatgridMarker
}

type cueMsg struct {
	Time   float32 `msgpack:"Time"`
	Name   string  `msgpack:"Name"`
	Hotcue bool    `msgpack:"Hotcue"`
	Slot   uint8   `msgpack:"-"`
}

type cuesPayload struct {
	Hash [32]byte
	Cues []cueMsg
}

// prolink-clx's envelope predates TotalPackets.
type prolinkBinaryMessage struct {
	Type  string   `msgpack:"Type"`
	Hash  [32]byte `msgpack:"Hash"`
	Total uint64   `msgpack:"Total"`
	Order uint32   `msgpack:"Order"`
	Data  []byte   `msgpack:"Data"`
}

func put(name string, packetType byte, v any) {
	b, err := msgpack.Marshal(v)
	if err != nil {
		panic(err)
	}
	fmt.Printf("%s\t%s\n", name, hex.EncodeToString(append([]byte{packetType}, b...)))
}

func trackHash() [32]byte {
	sum := md5.Sum([]byte(title))
	var out [32]byte
	hex.Encode(out[:], sum[:])
	return out
}

func envelopeHash(payload []byte) [32]byte {
	sum := sha256.Sum256(payload)
	var out [32]byte
	hex.Encode(out[:], sum[:16])
	return out
}

func le32(b []byte, v uint32) []byte {
	return append(b, byte(v), byte(v>>8), byte(v>>16), byte(v>>24))
}

func rwf() []byte {
	var b []byte
	for _, v := range []uint32{2, 1, sampleRate, samplesPerP, 8, pixelCount} {
		b = le32(b, v)
	}
	for i := 0; i < pixelCount; i++ {
		v := int8(i % 100)
		b = append(b, byte(-v), byte(v))
	}
	b = append(b, 'C', 'L', 'R', 'S')
	b = le32(b, pixelCount)
	for i := 0; i < pixelCount; i++ {
		b = append(b, byte(i), byte(i*2), byte(i*3))
	}
	return b
}

// The same fragmenting as the Server's sendChunkedBytes.
func chunked(name, msgType string, payload []byte) {
	msg := binaryMessage{
		Type:         msgType,
		Hash:         envelopeHash(payload),
		Total:        uint64(len(payload)),
		TotalPackets: uint32((len(payload) + chunkSize - 1) / chunkSize),
	}
	for start := 0; start < len(payload); start += chunkSize {
		end := min(start+chunkSize, len(payload))
		msg.Data = payload[start:end]
		put(fmt.Sprintf("%s[%d]", name, msg.Order), 0x05, msg)
		msg.Order++
	}
}

func marshal(v any) []byte {
	b, err := msgpack.Marshal(v)
	if err != nil {
		panic(err)
	}
	return b
}

func main() {
	put("Deck", 0x01, deck{
		Pitch: 1.02, Position: 12.5, Position2: 33.25, NormalizedPosition: 0.1,
		BPM: 128, Length: 240, EQLow: 0.5, EQMid: 0.625, EQHigh: 0.75, Deck: 2, Beat: 3,
	})
	put("Control", 0x00, control{
		UpfaderA: 1, UpfaderB: 0.5, UpfaderC: 0.25, Crossfader: 0.75,
		SeratoActive: 2, AppState: "Production",
	})
	put("Meta", 0x02, meta{
		Deck: 2, Title: title, Artist: "Ünicode Artist", Album: "Album", FilePath: "/music/track.flac",
	})
	put("LoopEnter", 0x04, deckEvent{Event: "loop_enter", Deck: 2, Values: []any{float32(32.5), float32(40)}})
	put("LoopEnterTraktor", 0x04, deckEvent{Event: "loop_enter", Deck: 1, Values: []any{float32(4)}})
	put("LoopExit", 0x04, deckEvent{Event: "loop_exit", Deck: 2, Values: nil})
	put("Command", 0x04, deckEvent{Event: "hotcue_1", Deck: 0, Values: []any{uint8(7)}})

	chunked("Waveform", "waveform", marshal(waveformData{Hash: trackHash(), Data: rwf()}))
	chunked("Beatgrid", "beatgrid", marshal(beatgridPayload{
		Hash:  trackHash(),
		Total: 512,
		Markers: []beatgridMarker{
			{Bpm: 128, Position: 0.25, BeatsToNext: 64},
			{Bpm: 130, Position: 30.25, BeatsToNext: 32},
			{Bpm: 130, Position: 45, Terminal: true},
		},
	}))
	chunked("Cues", "cues", marshal(cuesPayload{
		Hash: trackHash(),
		Cues: []cueMsg{
			{Time: 1.5, Name: "Intro", Hotcue: true, Slot: 1},
			{Time: 96, Name: "", Hotcue: false},
		},
	}))
	chunked("CuesNil", "cues", marshal(cuesPayload{Hash: trackHash()}))

	// prolink-clx: no TotalPackets, so a receiver completes on the byte total.
	payload := marshal(waveformData{Hash: trackHash(), Data: rwf()})
	msg := prolinkBinaryMessage{Type: "waveform", Hash: trackHash(), Total: uint64(len(payload))}
	for start := 0; start < len(payload); start += chunkSize {
		end := min(start+chunkSize, len(payload))
		msg.Data = payload[start:end]
		put(fmt.Sprintf("ProlinkWaveform[%d]", msg.Order), 0x05, msg)
		msg.Order++
	}
}
