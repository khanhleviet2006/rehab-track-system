/* ═══════════════════════════════════════════════════════════════
   REHAB FLIGHT — game.js
   Di chuyển khinh khí cầu theo góc từ cảm biến (C# → JS)
   Moving Average filter + Isometric Hold timer + Scoring
═══════════════════════════════════════════════════════════════ */

'use strict';

// ── Constants ──────────────────────────────────────────────────
const ANGLE_MIN        = 0;
const ANGLE_MAX        = 180;
const ZONE_LOW         = 85;
const ZONE_HIGH        = 95;
const HOLD_DURATION_MS = 5000;          // 5 seconds
const MA_WINDOW        = 10;            // moving-average window
const PADDING_PX       = 80;            // top/bottom margin inside canvas

// Arc geometry (SVG arc panel)
const ARC_CX = 60, ARC_CY = 70, ARC_R = 55;

// ── State ──────────────────────────────────────────────────────
const state = {
  rawAngle:      90,
  smoothedAngle: 90,
  angleBuffer:   [],          // MA buffer
  inZone:        false,
  holdStart:     null,        // timestamp when hold started
  holdElapsed:   0,           // ms elapsed in current hold
  score:         0,
  sessionStart:  Date.now(),
  totalInZoneMs: 0,
  lastTickMs:    null,
  running:       false,
};

// ── DOM refs ───────────────────────────────────────────────────
const balloon        = document.getElementById('balloon');
const safeZone       = document.getElementById('safeZone');
const angleIndicator = document.getElementById('angleIndicator');
const holdRing       = document.getElementById('holdRing');
const ringFill       = document.getElementById('ringFill');
const ringLabel      = document.getElementById('ringLabel');
const hudScore       = document.getElementById('hudScore');
const hudAngle       = document.getElementById('hudAngle');
const arcNeedle      = document.getElementById('arcNeedle');
const arcZone        = document.getElementById('arcZone');
const arcAngleText   = document.getElementById('arcAngleText');

// Summary
const summaryOverlay = document.getElementById('summaryOverlay');
const resScore       = document.getElementById('res-score');
const resTime        = document.getElementById('res-time');
const resHolds       = document.getElementById('res-holds');
const resRomPct      = document.getElementById('res-rom-pct');
const resRomBar      = document.getElementById('res-rom-bar');
const summaryDate    = document.getElementById('summaryDate');

// ── Ring circumference (r=30) ──────────────────────────────────
const RING_CIRC = 2 * Math.PI * 30; // ≈ 188.5

// ──────────────────────────────────────────────────────────────
//  Moving-Average filter
// ──────────────────────────────────────────────────────────────
function pushAngle(raw) {
  state.angleBuffer.push(raw);
  if (state.angleBuffer.length > MA_WINDOW) {
    state.angleBuffer.shift();
  }
  const sum = state.angleBuffer.reduce((a, b) => a + b, 0);
  return sum / state.angleBuffer.length;
}

// ──────────────────────────────────────────────────────────────
//  Angle → Y position (px from top of canvas)
//  0° → top (PADDING_PX), 180° → bottom (canvasH - PADDING_PX)
// ──────────────────────────────────────────────────────────────
function angleToY(angle) {
  const canvas = document.getElementById('gameCanvas');
  const h = canvas.clientHeight;
  const travel = h - PADDING_PX * 2 - 100; // 100 = balloon height
  const t = (angle - ANGLE_MIN) / (ANGLE_MAX - ANGLE_MIN);
  return PADDING_PX + t * travel;
}

// ──────────────────────────────────────────────────────────────
//  Safe-zone band position
// ──────────────────────────────────────────────────────────────
function positionSafeZone() {
  const yTop = angleToY(ZONE_LOW);
  const yBot = angleToY(ZONE_HIGH);
  safeZone.style.top    = yTop + 'px';
  safeZone.style.height = (yBot - yTop) + 'px';
}

// ──────────────────────────────────────────────────────────────
//  Arc panel: compute SVG path for a given angle (0–180°)
//  maps 0° → leftmost point, 180° → rightmost point
// ──────────────────────────────────────────────────────────────
function angleToPolar(deg) {
  // 0° = left (180° in standard math), 180° = right (0° in standard math)
  const rad = ((180 - deg) / 180) * Math.PI;
  return {
    x: ARC_CX + ARC_R * Math.cos(rad),
    y: ARC_CY - ARC_R * Math.sin(rad),
  };
}

function buildArcZonePath() {
  const p1 = angleToPolar(ZONE_LOW);
  const p2 = angleToPolar(ZONE_HIGH);
  return `M ${p1.x.toFixed(2)},${p1.y.toFixed(2)} A ${ARC_R},${ARC_R} 0 0 1 ${p2.x.toFixed(2)},${p2.y.toFixed(2)}`;
}

// ──────────────────────────────────────────────────────────────
//  Exhaust particle effect
// ──────────────────────────────────────────────────────────────
let exhaustTimer = 0;
function spawnExhaust() {
  const rect = balloon.getBoundingClientRect();
  const p = document.createElement('div');
  p.className = 'exhaust-particle';
  const size = 6 + Math.random() * 8;
  p.style.cssText = `
    width:${size}px; height:${size}px;
    left:${rect.left + rect.width / 2 - size / 2 + (Math.random() - .5) * 20}px;
    top:${rect.bottom - 20}px;
    background: radial-gradient(circle, rgba(255,200,80,.8), rgba(255,100,0,.4));
    box-shadow: 0 0 8px rgba(255,150,0,.5);
  `;
  document.body.appendChild(p);
  setTimeout(() => p.remove(), 1200);
}

// ──────────────────────────────────────────────────────────────
//  Score popup
// ──────────────────────────────────────────────────────────────
function showScorePopup() {
  const rect = balloon.getBoundingClientRect();
  const pop = document.createElement('div');
  pop.className = 'score-popup';
  pop.textContent = '+1';
  pop.style.left = (rect.left + rect.width / 2 - 30) + 'px';
  pop.style.top  = (rect.top - 10) + 'px';
  document.body.appendChild(pop);
  setTimeout(() => pop.remove(), 1400);
}

// ──────────────────────────────────────────────────────────────
//  Render frame
// ──────────────────────────────────────────────────────────────
function render() {
  const angle = state.smoothedAngle;
  const yPos  = angleToY(angle);

  // Balloon position
  balloon.style.top = yPos + 'px';

  // Angle indicator line
  angleIndicator.style.top = (yPos + 50) + 'px'; // mid-balloon

  // HUD
  hudAngle.textContent = angle.toFixed(1) + '°';
  hudScore.textContent = state.score;

  // Arc needle rotation
  // map angle 0→180 to CSS rotation 0→180° around pivot (60,70)
  const needleRot = -90 + angle; // 0° → needle pointing left, 90° → up, 180° → right
  arcNeedle.style.transform = `rotate(${needleRot}deg)`;
  arcAngleText.textContent  = angle.toFixed(0) + '°';

  // Zone state
  const inZone = angle >= ZONE_LOW && angle <= ZONE_HIGH;
  if (inZone !== state.inZone) {
    state.inZone = inZone;
    safeZone.classList.toggle('active', inZone);
    arcZone.classList.toggle('active', inZone);
    balloon.classList.toggle('in-zone', inZone);
    holdRing.classList.toggle('visible', inZone);
  }
}

// ──────────────────────────────────────────────────────────────
//  Hold timer tick
// ──────────────────────────────────────────────────────────────
function tickHold(nowMs) {
  if (!state.inZone) {
    // Reset hold if exited zone
    state.holdStart   = null;
    state.holdElapsed = 0;
    ringFill.style.strokeDashoffset = RING_CIRC;
    ringLabel.textContent = 'GIỮ VỊ TRÍ';
    return;
  }

  if (state.holdStart === null) {
    state.holdStart = nowMs;
  }

  state.holdElapsed = nowMs - state.holdStart;

  // Track in-zone time
  if (state.lastTickMs !== null) {
    state.totalInZoneMs += nowMs - state.lastTickMs;
  }
  state.lastTickMs = nowMs;

  const progress = Math.min(state.holdElapsed / HOLD_DURATION_MS, 1);
  const offset   = RING_CIRC * (1 - progress);
  ringFill.style.strokeDashoffset = offset;

  const remaining = Math.max(0, HOLD_DURATION_MS - state.holdElapsed);
  ringLabel.textContent = remaining > 0
    ? (remaining / 1000).toFixed(1) + 's'
    : '✓';

  // Completed hold
  if (state.holdElapsed >= HOLD_DURATION_MS) {
    state.score++;
    hudScore.textContent = state.score;
    showScorePopup();
    // Reset hold timer for next repetition
    state.holdStart   = nowMs;
    state.holdElapsed = 0;
  }
}

// ──────────────────────────────────────────────────────────────
//  RAF game loop
// ──────────────────────────────────────────────────────────────
let lastFrameMs = 0;
function gameLoop(nowMs) {
  if (!state.running) return;

  const dt = nowMs - lastFrameMs;
  lastFrameMs = nowMs;

  // Exhaust particles every ~120ms
  exhaustTimer += dt;
  if (exhaustTimer >= 120) {
    spawnExhaust();
    exhaustTimer = 0;
  }

  if (!state.inZone) {
    state.lastTickMs = null;
  }

  tickHold(nowMs);
  render();

  requestAnimationFrame(gameLoop);
}

// ──────────────────────────────────────────────────────────────
//  PUBLIC API — called by C# via WebView2
//  window.updateAngle(rawAngle: number)
// ──────────────────────────────────────────────────────────────
window.startGame = function() {
  if (!state.running) {
    state.running = true;
    state.sessionStart = Date.now(); // Đặt lại mốc thời gian bắt đầu phiên tập
    lastFrameMs = performance.now(); // Reset bộ đếm delta time
    requestAnimationFrame(gameLoop);
  }
};

window.stopGame = function() {
  state.running = false;
};
// ──────────────────────────────────────────────────────────────
//  Summary
// ──────────────────────────────────────────────────────────────
function showSummary() {
  state.running = false;
  holdRing.classList.remove('visible');

  const elapsed = Math.round((Date.now() - state.sessionStart) / 1000);
  const pct     = elapsed > 0
    ? Math.round((state.totalInZoneMs / (elapsed * 1000)) * 100)
    : 0;

  resScore.textContent  = state.score;
  resTime.textContent   = elapsed;
  resHolds.textContent  = state.score;
  resRomPct.textContent = pct + '%';
  resRomBar.style.width = Math.min(pct, 100) + '%';
  summaryDate.textContent = new Date().toLocaleDateString('vi-VN');

  summaryOverlay.classList.add('show');
  summaryOverlay.style.display = 'flex';
}

function restartGame() {
  Object.assign(state, {
    rawAngle:      90,
    smoothedAngle: 90,
    angleBuffer:   [],
    inZone:        false,
    holdStart:     null,
    holdElapsed:   0,
    score:         0,
    sessionStart:  Date.now(),
    totalInZoneMs: 0,
    lastTickMs:    null,
    running:       true,
  });
  hudScore.textContent = '0';
  hudAngle.textContent = '—°';
  ringFill.style.strokeDashoffset = RING_CIRC;
  summaryOverlay.style.display = 'none';
  summaryOverlay.classList.remove('show');
  requestAnimationFrame(gameLoop);
}

document.getElementById('btnRestart').addEventListener('click', restartGame);
document.getElementById('btnClose').addEventListener('click', () => {
  summaryOverlay.style.display = 'none';
  summaryOverlay.classList.remove('show');
  // Notify C# if needed: window.chrome.webview?.postMessage('close');
});

// Keyboard shortcut: press S to show summary (for testing)
document.addEventListener('keydown', e => {
  if (e.key === 's' || e.key === 'S') showSummary();
});

// ──────────────────────────────────────────────────────────────
//  Init
// ──────────────────────────────────────────────────────────────
(function init() {
  // Build arc zone path
  arcZone.setAttribute('d', buildArcZonePath());

  // Position safe zone on resize
  positionSafeZone();
  window.addEventListener('resize', positionSafeZone);

  // Hide loading screen
  const loadingScreen = document.getElementById('loadingScreen');
  setTimeout(() => {
    loadingScreen.classList.add('fade-out');
    setTimeout(() => { loadingScreen.style.display = 'none'; }, 650);
  }, 2000);
})();