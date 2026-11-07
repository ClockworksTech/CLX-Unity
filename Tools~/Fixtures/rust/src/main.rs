//! Prints CLX datagrams as "name<TAB>hex" lines, encoded by `rmp_serde` with the
//! struct shapes of the Rust senders: ClockworksBridge (src/clx.rs), and
//! ClockworksViewer3 (src/types.rs) for what it transmits itself. Keep the
//! structs field for field with those files.

use md5::{Digest, Md5};
use serde::Serialize;
use sha2::Sha256;

const TITLE: &str = "Déjà Vu (Extended Mix)";
const CHUNK: usize = 1200;
const PIXELS: u32 = 2000;

#[derive(Serialize)]
struct DeckPacket {
    #[serde(rename = "Pitch")]
    pitch: f64,
    #[serde(rename = "Position")]
    position: f64,
    #[serde(rename = "Position2")]
    position2: f64,
    #[serde(rename = "NormalizedPosition")]
    normalized_position: f64,
    #[serde(rename = "BPM")]
    bpm: f64,
    #[serde(rename = "Length")]
    length: f64,
    #[serde(rename = "EQLow")]
    eq_low: f32,
    #[serde(rename = "EQMid")]
    eq_mid: f32,
    #[serde(rename = "EQHigh")]
    eq_high: f32,
    #[serde(rename = "Deck")]
    deck: u8,
    #[serde(rename = "Beat")]
    beat: u8,
    #[serde(rename = "State")]
    play_state: u8,
    #[serde(rename = "Ord", default)]
    ord: u8,
}

#[derive(Serialize)]
struct MetaPacket {
    #[serde(rename = "Deck")]
    deck: u8,
    #[serde(rename = "Title")]
    title: String,
    #[serde(rename = "Artist")]
    artist: String,
    #[serde(rename = "Album")]
    album: String,
    #[serde(rename = "FilePath")]
    file_path: String,
}

#[derive(Serialize)]
struct ControlPacket {
    #[serde(rename = "UpfaderA")]
    upfader_a: f64,
    #[serde(rename = "UpfaderB")]
    upfader_b: f64,
    #[serde(rename = "UpfaderC")]
    upfader_c: f64,
    #[serde(rename = "UpfaderD")]
    upfader_d: f64,
    #[serde(rename = "UpfaderE")]
    upfader_e: f64,
    #[serde(rename = "UpfaderF")]
    upfader_f: f64,
    #[serde(rename = "Crossfader")]
    crossfader: f64,
    #[serde(rename = "Active")]
    active: u8,
    #[serde(rename = "AppState")]
    app_state: String,
}

#[derive(Serialize)]
struct EventPacket {
    #[serde(rename = "Event")]
    event: String,
    #[serde(rename = "Deck")]
    deck: u8,
    #[serde(rename = "Values")]
    values: Vec<f32>,
}

#[derive(Serialize)]
struct BinaryMessage {
    #[serde(rename = "Type")]
    msg_type: String,
    #[serde(rename = "Hash")]
    #[serde(with = "serde_bytes")]
    hash: Vec<u8>,
    #[serde(rename = "Total")]
    total: u64,
    #[serde(rename = "Order")]
    order: u32,
    #[serde(rename = "TotalPackets")]
    total_packets: u32,
    #[serde(rename = "Data")]
    #[serde(with = "serde_bytes")]
    data: Vec<u8>,
}

#[derive(Serialize)]
struct WaveformPayload {
    #[serde(rename = "Hash")]
    #[serde(with = "serde_bytes")]
    hash: Vec<u8>,
    #[serde(rename = "Data")]
    #[serde(with = "serde_bytes")]
    data: Vec<u8>,
    #[serde(rename = "Replace")]
    replace: bool,
}

#[derive(Serialize)]
struct BeatgridMarkerMsg {
    #[serde(rename = "Bpm")]
    bpm: f32,
    #[serde(rename = "Position")]
    position: f32,
    #[serde(rename = "Terminal")]
    terminal: bool,
    #[serde(rename = "BeatsToNext")]
    beats_to_next: u32,
}

#[derive(Serialize)]
struct BeatgridPayload {
    #[serde(rename = "Hash")]
    #[serde(with = "serde_bytes")]
    hash: Vec<u8>,
    #[serde(rename = "Total")]
    total: u32,
    #[serde(rename = "Markers")]
    markers: Vec<BeatgridMarkerMsg>,
}

#[derive(Serialize)]
struct CueMsg {
    #[serde(rename = "Time")]
    time: f32,
    #[serde(rename = "Name")]
    name: String,
    #[serde(rename = "Hotcue")]
    hotcue: bool,
}

#[derive(Serialize)]
struct CuesPayload {
    #[serde(rename = "Hash")]
    #[serde(with = "serde_bytes")]
    hash: Vec<u8>,
    #[serde(rename = "Cues")]
    cues: Vec<CueMsg>,
}

/// The Viewer's envelope: unlike the Bridge's, `Hash` has no `serde_bytes`, so
/// it goes out as an array of 32 integers rather than as `bin`.
#[derive(Serialize)]
#[allow(non_snake_case)]
struct ClxDataPacket {
    Type: String,
    Hash: [u8; 32],
    Total: u64,
    Order: u32,
    TotalPackets: u32,
    #[serde(with = "serde_bytes")]
    Data: Vec<u8>,
}

#[derive(Serialize)]
#[allow(non_snake_case)]
struct ClxLeaderDataPacket {
    LiveDeck: u8,
    Position: f64,
    Project: String,
}

/// With the spec's optional `Tx`, which the Viewer doesn't send yet.
#[derive(Serialize)]
#[allow(non_snake_case)]
struct LeaderWithTx {
    LiveDeck: u8,
    Position: f64,
    Project: String,
    Tx: u64,
}

fn put<T: Serialize>(name: &str, packet_type: u8, value: &T) {
    let mut bytes = vec![packet_type];
    bytes.extend(rmp_serde::to_vec_named(value).unwrap());
    println!("{}\t{}", name, hex::encode(bytes));
}

fn track_hash() -> Vec<u8> {
    hex::encode(Md5::digest(TITLE.as_bytes())).into_bytes()
}

fn rwf() -> Vec<u8> {
    let mut b = Vec::new();
    for v in [2u32, 1, 44100, 124, 8, PIXELS] {
        b.extend(v.to_le_bytes());
    }
    for i in 0..PIXELS {
        let v = (i % 100) as i8;
        b.push((-v) as u8);
        b.push(v as u8);
    }
    b.extend(b"CLRS");
    b.extend(PIXELS.to_le_bytes());
    for i in 0..PIXELS {
        b.push(i as u8);
        b.push((i * 2) as u8);
        b.push((i * 3) as u8);
    }
    b
}

/// The Bridge's `envelope_hash`: hex of the first 128 bits of the payload's SHA-256.
fn envelope_hash(payload: &[u8]) -> Vec<u8> {
    hex::encode(&Sha256::digest(payload)[..16]).into_bytes()
}

/// The Bridge's fragmenting.
fn chunked(name: &str, msg_type: &str, payload: &[u8]) {
    let total_packets = payload.len().div_ceil(CHUNK) as u32;
    let hash = envelope_hash(payload);
    for (order, chunk) in payload.chunks(CHUNK).enumerate() {
        put(
            &format!("{}[{}]", name, order),
            0x05,
            &BinaryMessage {
                msg_type: msg_type.to_string(),
                hash: hash.clone(),
                total: payload.len() as u64,
                order: order as u32,
                total_packets,
                data: chunk.to_vec(),
            },
        );
    }
}

fn main() {
    put(
        "Deck",
        0x01,
        &DeckPacket {
            pitch: 1.02,
            position: 12.5,
            position2: 33.25,
            normalized_position: 0.1,
            bpm: 128.0,
            length: 240.0,
            eq_low: 0.5,
            eq_mid: 0.625,
            eq_high: 0.75,
            deck: 2,
            beat: 3,
            play_state: 3,
            ord: 200,
        },
    );
    put(
        "Control",
        0x00,
        &ControlPacket {
            upfader_a: 1.0,
            upfader_b: 0.5,
            upfader_c: 0.25,
            upfader_d: 0.0,
            upfader_e: 0.125,
            upfader_f: 0.0625,
            crossfader: 0.75,
            active: 2,
            app_state: "Production".to_string(),
        },
    );
    put(
        "Meta",
        0x02,
        &MetaPacket {
            deck: 2,
            title: TITLE.to_string(),
            artist: "Ünicode Artist".to_string(),
            album: "Album".to_string(),
            file_path: "/music/track.flac".to_string(),
        },
    );
    put(
        "LoopEnter",
        0x04,
        &EventPacket { event: "loop_enter".to_string(), deck: 2, values: vec![32.5, 40.0] },
    );
    put(
        "LoopExit",
        0x04,
        &EventPacket { event: "loop_exit".to_string(), deck: 2, values: vec![] },
    );

    let waveform = WaveformPayload { hash: track_hash(), data: rwf(), replace: true };
    chunked("Waveform", "waveform", &rmp_serde::to_vec_named(&waveform).unwrap());
    let grid = BeatgridPayload {
        hash: track_hash(),
        total: 512,
        markers: vec![
            BeatgridMarkerMsg { bpm: 128.0, position: 0.25, terminal: false, beats_to_next: 64 },
            BeatgridMarkerMsg { bpm: 130.0, position: 30.25, terminal: false, beats_to_next: 32 },
            BeatgridMarkerMsg { bpm: 130.0, position: 45.0, terminal: true, beats_to_next: 0 },
        ],
    };
    chunked("Beatgrid", "beatgrid", &rmp_serde::to_vec_named(&grid).unwrap());
    let cues = CuesPayload {
        hash: track_hash(),
        cues: vec![
            CueMsg { time: 1.5, name: "Intro".to_string(), hotcue: true },
            CueMsg { time: 96.0, name: String::new(), hotcue: false },
        ],
    };
    chunked("Cues", "cues", &rmp_serde::to_vec_named(&cues).unwrap());

    let mut hash = [0u8; 32];
    hash.copy_from_slice(&track_hash());
    let inner = rmp_serde::to_vec_named(&waveform).unwrap();
    let packets = inner.len().div_ceil(CHUNK) as u32;
    for (order, chunk) in inner.chunks(CHUNK).enumerate() {
        put(
            &format!("ViewerWaveform[{}]", order),
            0x05,
            &ClxDataPacket {
                Type: "waveform".to_string(),
                Hash: hash,
                Total: inner.len() as u64,
                Order: order as u32,
                TotalPackets: packets,
                Data: chunk.to_vec(),
            },
        );
    }

    let leader = rmp_serde::to_vec_named(&ClxLeaderDataPacket {
        LiveDeck: 2,
        Position: 123.5,
        Project: "Main Stage".to_string(),
    })
    .unwrap();
    put(
        "Leader",
        0x05,
        &ClxDataPacket {
            Type: "leader".to_string(),
            Hash: [0u8; 32],
            Total: leader.len() as u64,
            Order: 0,
            TotalPackets: 1,
            Data: leader,
        },
    );
    let leader = rmp_serde::to_vec_named(&LeaderWithTx {
        LiveDeck: 2,
        Position: 123.5,
        Project: String::new(),
        Tx: 1_790_000_000_123_456_789,
    })
    .unwrap();
    put(
        "LeaderTx",
        0x05,
        &ClxDataPacket {
            Type: "leader".to_string(),
            Hash: [0u8; 32],
            Total: leader.len() as u64,
            Order: 0,
            TotalPackets: 1,
            Data: leader,
        },
    );
}
