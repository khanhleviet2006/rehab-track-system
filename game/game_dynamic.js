'use strict';
/*PHI THUYỀN KHÔNG GIAN – Dynamic ROM  |  RehabTrack v1.0
   Tích hợp: C# WPF WebView2 + MPU6050 Elbow Sensor
   Kiến trúc: OOP ES6 – Pure HTML5 Canvas – 60 FPS */

const CONFIG = {
  TARGET_ROM:        140,
  REP_HIGH_THRESH:   0.90,
  REP_LOW_THRESH:    0.20,
  LERP_FACTOR:       0.10,
  MAX_HEALTH:        5,
  COIN_SPAWN_RATE:   90,
  ASTEROID_SPAWN_RATE: 150,
  BONUS_ANGLE_TOL:   8,
  SCROLL_SPEED_BASE: 2.5,
  SCROLL_SPEED_MAX:  6,
  PARALLAX_LAYERS:   3,
  STAR_COUNT:        200,
  NEBULA_COUNT:      5,
};
const lerp  = (a, b, t) => a + (b - a) * t;
const clamp = (v, lo, hi) => Math.max(lo, Math.min(hi, v));
const rand  = (lo, hi) => Math.random() * (hi - lo) + lo;
const randInt = (lo, hi) => Math.floor(rand(lo, hi + 1));

function hexToRgba(hex, a) {
  const r = parseInt(hex.slice(1,3), 16);
  const g = parseInt(hex.slice(3,5), 16);
  const b = parseInt(hex.slice(5,7), 16);
  return `rgba(${r},${g},${b},${a})`;
}

/*CLASS: MedicalMetrics  (Logic Y Khoa)*/
class MedicalMetrics {
  constructor(targetRom = CONFIG.TARGET_ROM) {
    this.targetRom   = targetRom;
    this.reps        = 0;
    this.peakRom     = 0;
    this.romReadings = [];
    this.sessionStart = Date.now();

    this._phase = 'waiting'; 
    this._highThresh = targetRom * CONFIG.REP_HIGH_THRESH;
    this._lowThresh  = targetRom * CONFIG.REP_LOW_THRESH;
    this._peakThisFlex = 0;
  }

  update(angle) {
    this.romReadings.push(angle);
    if (angle > this.peakRom) this.peakRom = angle;

    let repCompleted = false;

    switch (this._phase) {
      case 'waiting':
        if (angle >= this._highThresh) {
          this._phase = 'flexing';
          this._peakThisFlex = angle;
        }
        break;

      case 'flexing':
        if (angle > this._peakThisFlex) this._peakThisFlex = angle;
        if (angle <= this._lowThresh) {
          this._phase = 'extending';
        }
        break;

      case 'extending':
        if (angle <= this._lowThresh) {
          this.reps++;
          repCompleted = true;
          this._phase = 'waiting';
          this._peakThisFlex = 0;
        }
        if (angle >= this._highThresh) {
          this._phase = 'flexing';
          this._peakThisFlex = angle;
        }
        break;
    }

    return repCompleted;
  }

  get avgRom() {
    if (this.romReadings.length === 0) return 0;
    const sum = this.romReadings.reduce((a, b) => a + b, 0);
    return Math.round(sum / this.romReadings.length);
  }

  get sessionSeconds() {
    return Math.round((Date.now() - this.sessionStart) / 1000);
  }

  get grade() {
    const pct = (this.peakRom / this.targetRom) * 100;
    if (pct >= 90) return 'Xuất Sắc';
    if (pct >= 75) return 'Tốt';
    if (pct >= 55) return 'Trung Bình';
    return 'Cần Cố Gắng';
  }

  export() {
    return {
      timestamp:     new Date().toISOString(),
      sessionId:     `ROM-${Date.now()}`,
      targetRom:     this.targetRom,
      peakRom:       this.peakRom,
      avgRom:        this.avgRom,
      reps:          this.reps,
      sessionTime:   this.sessionSeconds,
      grade:         this.grade,
      romTimeSeries: this.romReadings,
    };
  }
}

/* CLASS: Star  (Nền ngôi sao – Parallax) */
class Star {
  constructor(W, H, layer) {
    this.W = W; this.H = H;
    this.layer = layer; 
    this.reset(true);
  }

  reset(randomY = false) {
    this.x     = rand(0, this.W);
    this.y     = randomY ? rand(0, this.H) : -4;
    this.size  = rand(0.3, 1.4) * (this.layer * 0.6 + 0.5);
    this.speed = rand(0.2, 0.8) * (this.layer + 1);
    this.alpha = rand(0.3, 1.0);
    this.twinkle = rand(0.005, 0.025);
    this.twinkleDir = 1;
    const r = randInt(180,255), g = randInt(180,255);
    this.color = `rgb(${r},${g},255)`;
  }

  update() {
    this.y += this.speed;
    this.alpha += this.twinkle * this.twinkleDir;
    if (this.alpha > 1)   { this.alpha = 1;   this.twinkleDir = -1; }
    if (this.alpha < 0.2) { this.alpha = 0.2; this.twinkleDir =  1; }
    if (this.y > this.H) this.reset();
  }

  draw(ctx) {
    ctx.save();
    ctx.globalAlpha = this.alpha;
    ctx.fillStyle = this.color;
    ctx.shadowColor = this.color;
    ctx.shadowBlur = this.size * 3;
    ctx.beginPath();
    ctx.arc(this.x, this.y, this.size, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }
}

/*CLASS: Nebula  (Đám mây tinh vân trang trí) */
class Nebula {
  constructor(W, H) {
    this.W = W; this.H = H;
    this.reset(true);
  }

  reset(randomY = false) {
    this.x     = rand(0, this.W);
    this.y     = randomY ? rand(-this.H * 0.5, this.H) : -200;
    this.rX    = rand(80, 200);
    this.rY    = rand(50, 130);
    this.speed = rand(0.15, 0.4);
    this.alpha = rand(0.03, 0.10);
    const palette = ['#1a0050','#002244','#001a3a','#00442a','#220044'];
    this.color = palette[randInt(0, palette.length - 1)];
  }

  update() {
    this.y += this.speed;
    if (this.y > this.H + 200) this.reset();
  }

  draw(ctx) {
    ctx.save();
    ctx.globalAlpha = this.alpha;
    const grd = ctx.createRadialGradient(
      this.x, this.y, 0,
      this.x, this.y, this.rX
    );
    grd.addColorStop(0, this.color);
    grd.addColorStop(1, 'transparent');
    ctx.fillStyle = grd;
    ctx.scale(1, this.rY / this.rX);
    ctx.beginPath();
    ctx.arc(this.x, this.y * (this.rX / this.rY), this.rX, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }
}

/*CLASS: Particle  (Hiệu ứng nổ & Thu thập) */
class Particle {
  constructor(x, y, color, type = 'explode') {
    this.x = x; this.y = y;
    this.color = color;
    this.type  = type;

    const speed = type === 'collect' ? rand(1, 4) : rand(2, 7);
    const angle = rand(0, Math.PI * 2);
    this.vx   = Math.cos(angle) * speed;
    this.vy   = Math.sin(angle) * speed - (type === 'collect' ? 2 : 0);
    this.life = 1.0;
    this.decay = rand(0.018, 0.045);
    this.size  = type === 'collect' ? rand(2, 5) : rand(1.5, 4);
    this.gravity = type === 'explode' ? 0.08 : -0.05;
  }

  update() {
    this.x   += this.vx;
    this.y   += this.vy;
    this.vy  += this.gravity;
    this.vx  *= 0.98;
    this.life -= this.decay;
    this.size *= 0.97;
  }

  draw(ctx) {
    if (this.life <= 0) return;
    ctx.save();
    ctx.globalAlpha = Math.max(0, this.life);
    ctx.fillStyle = this.color;
    ctx.shadowColor = this.color;
    ctx.shadowBlur  = 6;
    ctx.beginPath();
    ctx.arc(this.x, this.y, Math.max(0, this.size), 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
  }

  get dead() { return this.life <= 0 || this.size < 0.3; }
}

/*CLASS: Player  (Phi thuyền)*/
class Player {
  constructor(W, H) {
    this.W = W; this.H = H;
    this.x = W * 0.18;
    this.y = H * 0.5;
    this.targetY = H * 0.5;
    this.width   = 52;
    this.height  = 30;
    this.health  = CONFIG.MAX_HEALTH;
    this.score   = 0;
    this.invincible = 0; 
    this.thrustAnim = 0;
    this.shakeX = 0;
    this.shakeY = 0;
    this._trail = []; 
  }

  setTargetFromAngle(angle) {
    const clamped = clamp(angle, 0, CONFIG.TARGET_ROM);
    const pct = clamped / CONFIG.TARGET_ROM; 
    const margin = this.height * 1.5;
    this.targetY = lerp(this.H - margin, margin, pct);
  }

  update() {
    this.y = lerp(this.y, this.targetY, CONFIG.LERP_FACTOR);
    this._trail.unshift({ x: this.x - this.width * 0.4, y: this.y });
    if (this._trail.length > 14) this._trail.pop();
    this.thrustAnim += 0.25;
    if (this.invincible > 0) this.invincible--;
    this.shakeX *= 0.8;
    this.shakeY *= 0.8;
  }

  hit() {
    if (this.invincible > 0) return false;
    this.health--;
    this.invincible = 90;
    this.shakeX = 10;
    this.shakeY = 8;
    return true;
  }

  draw(ctx) {
    const px = this.x + this.shakeX * (Math.random() - 0.5) * 2;
    const py = this.y + this.shakeY * (Math.random() - 0.5) * 2;
    if (this.invincible > 0 && Math.floor(this.invincible / 6) % 2 === 0) return;

    ctx.save();
    ctx.translate(px, py);

    this._trail.forEach((pt, i) => {
      const alpha = (1 - i / this._trail.length) * 0.6;
      const size  = (1 - i / this._trail.length) * 7;
      ctx.globalAlpha = alpha;
      ctx.fillStyle = i < 5 ? '#00ffaa' : i < 9 ? '#0088ff' : '#ff4400';
      ctx.shadowColor = ctx.fillStyle;
      ctx.shadowBlur  = 10;
      ctx.beginPath();
      ctx.arc(pt.x - px, pt.y - py, size, 0, Math.PI * 2);
      ctx.fill();
    });
    ctx.globalAlpha = 1;
    ctx.shadowBlur  = 0;

    const flameLen = 18 + Math.sin(this.thrustAnim) * 6;
    const flameGrd = ctx.createLinearGradient(-this.width * 0.5 - flameLen, 0, -this.width * 0.5, 0);
    flameGrd.addColorStop(0, 'transparent');
    flameGrd.addColorStop(0.4, 'rgba(255,100,0,0.8)');
    flameGrd.addColorStop(1, 'rgba(255,220,0,1)');

    ctx.beginPath();
    ctx.moveTo(-this.width * 0.5, -6 - Math.sin(this.thrustAnim * 0.7) * 2);
    ctx.lineTo(-this.width * 0.5 - flameLen, 0);
    ctx.lineTo(-this.width * 0.5, 6 + Math.sin(this.thrustAnim * 0.7) * 2);
    ctx.closePath();
    ctx.fillStyle = flameGrd;
    ctx.fill();

    const shipGrd = ctx.createLinearGradient(
      -this.width * 0.5, -this.height * 0.5,
       this.width * 0.5,  this.height * 0.5
    );
    shipGrd.addColorStop(0, '#b0c8e8');
    shipGrd.addColorStop(0.5, '#e8f0ff');
    shipGrd.addColorStop(1, '#6080a0');

    ctx.beginPath();
    ctx.moveTo( this.width * 0.5,  0); 
    ctx.lineTo( this.width * 0.1, -this.height * 0.45);
    ctx.lineTo(-this.width * 0.5, -this.height * 0.2);
    ctx.lineTo(-this.width * 0.5,  this.height * 0.2);
    ctx.lineTo( this.width * 0.1,  this.height * 0.45);
    ctx.closePath();
    ctx.fillStyle = shipGrd;
    ctx.shadowColor = '#4488ff';
    ctx.shadowBlur  = 16;
    ctx.fill();

    const cockpitGrd = ctx.createRadialGradient(8, -4, 1, 8, -4, 12);
    cockpitGrd.addColorStop(0, 'rgba(120,220,255,0.9)');
    cockpitGrd.addColorStop(1, 'rgba(0,80,160,0.4)');
    ctx.beginPath();
    ctx.ellipse(8, 0, 12, 9, 0, 0, Math.PI * 2);
    ctx.fillStyle = cockpitGrd;
    ctx.shadowColor = '#00ccff';
    ctx.shadowBlur  = 10;
    ctx.fill();

    ctx.beginPath();
    ctx.moveTo( 0,   -this.height * 0.25);
    ctx.lineTo(-8,   -this.height * 0.85);
    ctx.lineTo(-this.width * 0.5, -this.height * 0.6);
    ctx.lineTo(-this.width * 0.3, -this.height * 0.2);
    ctx.closePath();
    ctx.fillStyle = 'rgba(80,140,220,0.9)';
    ctx.shadowBlur = 8;
    ctx.fill();

    ctx.beginPath();
    ctx.moveTo( 0,    this.height * 0.25);
    ctx.lineTo(-8,    this.height * 0.85);
    ctx.lineTo(-this.width * 0.5,  this.height * 0.6);
    ctx.lineTo(-this.width * 0.3,  this.height * 0.2);
    ctx.closePath();
    ctx.fillStyle = 'rgba(80,140,220,0.9)';
    ctx.fill();

    const blinkAlpha = 0.5 + Math.sin(this.thrustAnim * 0.8) * 0.5;
    ctx.globalAlpha = blinkAlpha;
    ctx.fillStyle = '#ff4444';
    ctx.shadowColor = '#ff0000';
    ctx.shadowBlur = 8;
    ctx.beginPath();
    ctx.arc(-this.width * 0.4, 0, 2.5, 0, Math.PI * 2);
    ctx.fill();
    ctx.fillStyle = '#44ff44';
    ctx.shadowColor = '#00ff00';
    ctx.beginPath();
    ctx.arc(this.width * 0.25, -4, 2, 0, Math.PI * 2);
    ctx.fill();

    ctx.restore();
  }

  get hitRadius() { return 14; }
  get collisionRect() {
    return {
      x: this.x - this.width * 0.35,
      y: this.y - this.height * 0.3,
      w: this.width * 0.65,
      h: this.height * 0.6,
    };
  }
}

/*CLASS: Coin  (Vật phẩm ăn điểm)*/
class Coin {
  constructor(W, H) {
    this.W = W; this.H = H;
    this.x = W + 30;
    this.y = rand(40, H - 40);
    this.r = rand(10, 16);
    this.speed = CONFIG.SCROLL_SPEED_BASE + rand(0, 1);
    this.anim  = rand(0, Math.PI * 2);
    this.value = 10;
    this.type  = 'normal';

    const roll = Math.random();
    if (roll > 0.85) {
      this.type  = 'gold';
      this.value = 30;
      this.r     = rand(13, 18);
    } else if (roll > 0.7) {
      this.type  = 'blue';
      this.value = 20;
    }
  }

  update(scrollMult = 1) {
    this.x   -= this.speed * scrollMult;
    this.anim += 0.06;
  }

  draw(ctx) {
    const pulse = 0.85 + Math.sin(this.anim) * 0.15;
    const r = this.r * pulse;

    const colors = {
      normal: { inner: '#ffd700', outer: '#ff8800', glow: '#ffcc00' },
      gold:   { inner: '#fff0a0', outer: '#ffaa00', glow: '#ffffff' },
      blue:   { inner: '#80cfff', outer: '#0044ff', glow: '#00aaff' },
    };
    const c = colors[this.type];

    ctx.save();
    const glowGrd = ctx.createRadialGradient(this.x, this.y, 0, this.x, this.y, r * 2.2);
    glowGrd.addColorStop(0, hexToRgba(c.glow, 0.25));
    glowGrd.addColorStop(1, 'transparent');
    ctx.fillStyle = glowGrd;
    ctx.beginPath();
    ctx.arc(this.x, this.y, r * 2.2, 0, Math.PI * 2);
    ctx.fill();

    const coinGrd = ctx.createRadialGradient(
      this.x - r * 0.3, this.y - r * 0.3, r * 0.1,
      this.x, this.y, r
    );
    coinGrd.addColorStop(0, c.inner);
    coinGrd.addColorStop(1, c.outer);
    ctx.beginPath();
    ctx.arc(this.x, this.y, r, 0, Math.PI * 2);
    ctx.fillStyle = coinGrd;
    ctx.shadowColor = c.glow;
    ctx.shadowBlur  = 16;
    ctx.fill();

    ctx.fillStyle = 'rgba(255,255,255,0.8)';
    ctx.font = `bold ${r * 0.85}px Courier New`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.shadowBlur = 0;
    ctx.fillText(this.type === 'gold' ? '★' : '+', this.x, this.y + 0.5);
    ctx.restore();
  }

  get dead() { return this.x < -50; }
  get hitRadius() { return this.r * 1.4; }
}

/* CLASS: Asteroid  (Chướng ngại vật)*/
class Asteroid {
  constructor(W, H) {
    this.W = W; this.H = H;
    this.x = W + 50;
    this.y = rand(40, H - 40);
    this.r = rand(18, 42);
    this.speed   = CONFIG.SCROLL_SPEED_BASE * rand(0.8, 1.6);
    this.rotSpeed = rand(-0.03, 0.03);
    this.angle   = 0;
    this._buildShape();
  }

  _buildShape() {
    this.points = [];
    const sides = randInt(7, 12);
    for (let i = 0; i < sides; i++) {
      const a = (i / sides) * Math.PI * 2;
      const r = this.r * rand(0.65, 1.0);
      this.points.push({ x: Math.cos(a) * r, y: Math.sin(a) * r });
    }
    const hue = randInt(20, 60);
    this.colorDark  = `hsl(${hue}, 30%, 22%)`;
    this.colorMid   = `hsl(${hue}, 25%, 38%)`;
    this.colorLight = `hsl(${hue}, 20%, 55%)`;
  }

  update(scrollMult = 1) {
    this.x     -= this.speed * scrollMult;
    this.angle += this.rotSpeed;
  }

  draw(ctx) {
    ctx.save();
    ctx.translate(this.x, this.y);
    ctx.rotate(this.angle);

    const glowGrd = ctx.createRadialGradient(0,0,0, 0,0, this.r*1.8);
    glowGrd.addColorStop(0, 'rgba(255,80,20,0.12)');
    glowGrd.addColorStop(1, 'transparent');
    ctx.fillStyle = glowGrd;
    ctx.beginPath();
    ctx.arc(0,0,this.r*1.8,0,Math.PI*2);
    ctx.fill();

    ctx.beginPath();
    this.points.forEach((pt, i) => {
      i === 0 ? ctx.moveTo(pt.x, pt.y) : ctx.lineTo(pt.x, pt.y);
    });
    ctx.closePath();

    const bodyGrd = ctx.createRadialGradient(-this.r*0.25, -this.r*0.25, 1, 0, 0, this.r);
    bodyGrd.addColorStop(0, this.colorLight);
    bodyGrd.addColorStop(0.6, this.colorMid);
    bodyGrd.addColorStop(1, this.colorDark);
    ctx.fillStyle = bodyGrd;
    ctx.shadowColor = '#ff5500';
    ctx.shadowBlur  = 12;
    ctx.fill();

    ctx.strokeStyle = 'rgba(255,120,50,0.4)';
    ctx.lineWidth = 1;
    ctx.stroke();

    ctx.strokeStyle = 'rgba(0,0,0,0.35)';
    ctx.lineWidth = 1;
    ctx.beginPath();
    ctx.moveTo(-this.r*0.2, -this.r*0.4);
    ctx.lineTo( this.r*0.1,  this.r*0.2);
    ctx.stroke();
    ctx.beginPath();
    ctx.moveTo( this.r*0.3, -this.r*0.2);
    ctx.lineTo(-this.r*0.1,  this.r*0.3);
    ctx.stroke();
    ctx.restore();
  }

  get dead() { return this.x < -80; }
  get hitRadius() { return this.r * 0.78; }
}

/*CLASS: BonusItem  (Vật phẩm tại góc Peak ROM)*/
class BonusItem {
  constructor(W, H, targetY) {
    this.W = W; this.H = H;
    this.x    = W + 40;
    this.y    = targetY;
    this.r    = 20;
    this.speed= CONFIG.SCROLL_SPEED_BASE * 0.9;
    this.anim = 0;
    this.value = 50;
  }

  update(scrollMult = 1) {
    this.x   -= this.speed * scrollMult;
    this.anim += 0.04;
  }

  draw(ctx) {
    const pulse = 0.88 + Math.sin(this.anim) * 0.12;
    const r = this.r * pulse;

    ctx.save();
    ctx.translate(this.x, this.y);

    for (let i = 0; i < 8; i++) {
      const a = this.anim + (i * Math.PI / 4);
      const ox = Math.cos(a) * (r + 10);
      const oy = Math.sin(a) * (r + 10);
      ctx.globalAlpha = 0.5 + Math.sin(this.anim * 3 + i) * 0.3;
      ctx.fillStyle = `hsl(${(i*45 + this.anim*30) % 360}, 100%, 70%)`;
      ctx.shadowColor = ctx.fillStyle;
      ctx.shadowBlur  = 8;
      ctx.beginPath();
      ctx.arc(ox, oy, 3, 0, Math.PI * 2);
      ctx.fill();
    }
    ctx.globalAlpha = 1;

    const glowGrd = ctx.createRadialGradient(0,0,0, 0,0, r*2.5);
    glowGrd.addColorStop(0, 'rgba(255,215,0,0.4)');
    glowGrd.addColorStop(0.5, 'rgba(255,100,200,0.2)');
    glowGrd.addColorStop(1, 'transparent');
    ctx.fillStyle = glowGrd;
    ctx.beginPath();
    ctx.arc(0,0,r*2.5,0,Math.PI*2);
    ctx.fill();

    const bodyGrd = ctx.createRadialGradient(-r*0.2, -r*0.2, r*0.1, 0, 0, r);
    bodyGrd.addColorStop(0, '#ffffa0');
    bodyGrd.addColorStop(0.5, '#ffcc00');
    bodyGrd.addColorStop(1, '#ff8800');
    ctx.beginPath();
    ctx.arc(0, 0, r, 0, Math.PI*2);
    ctx.fillStyle = bodyGrd;
    ctx.shadowColor = '#ffdd00';
    ctx.shadowBlur  = 24;
    ctx.fill();

    ctx.fillStyle = 'rgba(255,255,255,0.95)';
    ctx.font = `bold ${r*0.9}px Courier New`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.shadowBlur = 0;
    ctx.fillText('⚕', 0, 1);
    ctx.restore();
  }

  get dead() { return this.x < -60; }
  get hitRadius() { return this.r * 1.5; }
}

/*CLASS: CollisionManager */
class CollisionManager {
  static circle(ax, ay, ar, bx, by, br) {
    const dx = ax - bx, dy = ay - by;
    return (dx * dx + dy * dy) < (ar + br) * (ar + br);
  }

  static checkPlayer(player, items, radius) {
    const result = [];
    for (let i = items.length - 1; i >= 0; i--) {
      const item = items[i];
      if (CollisionManager.circle(
        player.x, player.y, radius,
        item.x,   item.y,   item.hitRadius
      )) {
        result.push({ item, index: i });
      }
    }
    return result;
  }
}

/*CLASS: HUD  (Heads-Up Display) */
class HUD {
  constructor(W, H) {
    this.W = W; this.H = H;
    this._scoreAnim   = 0;
    this._repFlash    = 0;
  }

  flashRep()   { this._repFlash    = 60; }
  flashScore() { this._scoreAnim  = 30; }

  draw(ctx, metrics, score, health, scrollSpeed, currentAngle, targetRom) {
    this._repFlash--;
    this._scoreAnim--;

    ctx.save();
    const barH = 56;
    const barGrd = ctx.createLinearGradient(0,0,0,barH);
    barGrd.addColorStop(0, 'rgba(2,8,16,0.92)');
    barGrd.addColorStop(1, 'rgba(2,8,16,0)');
    ctx.fillStyle = barGrd;
    ctx.fillRect(0, 0, this.W, barH + 10);

    const scoreScale = this._scoreAnim > 0 ? 1 + (this._scoreAnim / 30) * 0.15 : 1;
    ctx.font = `bold ${Math.round(22 * scoreScale)}px Courier New`;
    ctx.textAlign = 'left';
    ctx.fillStyle = this._scoreAnim > 0 ? '#ffe066' : '#e8f4f0';
    ctx.shadowColor = '#ffcc00';
    ctx.shadowBlur  = this._scoreAnim > 0 ? 14 : 0;
    ctx.fillText(`★ ${score.toLocaleString()}`, 18, 34);

    const hx = this.W / 2 - 80;
    ctx.shadowBlur = 0;
    ctx.font = '12px Courier New';
    ctx.fillStyle = '#7a9e90';
    ctx.textAlign = 'center';
    ctx.fillText('HP', this.W / 2, 16);
    for (let i = 0; i < CONFIG.MAX_HEALTH; i++) {
      const filled = i < health;
      const hpX = hx + i * 32;
      ctx.beginPath();
      ctx.roundRect(hpX, 22, 26, 14, 3);
      if (filled) {
        const hpGrd = ctx.createLinearGradient(hpX, 22, hpX + 26, 36);
        hpGrd.addColorStop(0, '#00ffaa');
        hpGrd.addColorStop(1, '#00b074');
        ctx.fillStyle = hpGrd;
        ctx.shadowColor = '#00ffaa';
        ctx.shadowBlur  = 6;
      } else {
        ctx.fillStyle = 'rgba(255,255,255,0.08)';
        ctx.shadowBlur = 0;
      }
      ctx.fill();
      ctx.strokeStyle = filled ? '#00b074' : 'rgba(255,255,255,0.12)';
      ctx.lineWidth = 1;
      ctx.stroke();
    }

    ctx.shadowBlur = 0;
    ctx.font = '11px Courier New';
    ctx.fillStyle = '#7a9e90';
    ctx.textAlign = 'right';
    ctx.fillText(`SPD ${scrollSpeed.toFixed(1)}x`, this.W - 16, 20);

    const repColor = this._repFlash > 0
      ? `hsl(${120 + Math.sin(this._repFlash * 0.3) * 40}, 100%, 65%)`
      : '#00b074';
    ctx.font = `bold 14px Courier New`;
    ctx.fillStyle = repColor;
    ctx.shadowColor = repColor;
    ctx.shadowBlur  = this._repFlash > 0 ? 16 : 4;
    ctx.fillText(`↺ ${metrics.reps} REPS`, this.W - 16, 38);

    this._drawRomGauge(ctx, currentAngle, targetRom);

    ctx.shadowBlur = 0;
    ctx.font = 'bold 13px Courier New';
    ctx.fillStyle = '#00b074';
    ctx.textAlign = 'left';
    ctx.fillText(`${Math.round(currentAngle)}°`, 18, this.H - 60);
    ctx.font = '11px Courier New';
    ctx.fillStyle = '#7a9e90';
    ctx.fillText('ELBOW ROM', 18, this.H - 44);

    ctx.font = 'bold 12px Courier New';
    ctx.fillStyle = '#ffd700';
    ctx.shadowColor = '#ffd700';
    ctx.shadowBlur  = 6;
    ctx.fillText(`PEAK ${Math.round(metrics.peakRom)}°`, 18, this.H - 22);

    ctx.restore();
  }

  _drawRomGauge(ctx, angle, targetRom) {
    const gX    = 14;
    const gY    = 80;
    const gH    = this.H - 160;
    const gW    = 8;
    const pct   = clamp(angle / targetRom, 0, 1);
    const fillH = gH * pct;
    const fillY = gY + gH - fillH;

    ctx.save();
    ctx.fillStyle = 'rgba(255,255,255,0.06)';
    ctx.beginPath();
    ctx.roundRect(gX, gY, gW, gH, 4);
    ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,0.1)';
    ctx.lineWidth = 1;
    ctx.stroke();

    if (fillH > 0) {
      const fillGrd = ctx.createLinearGradient(0, fillY + fillH, 0, fillY);
      fillGrd.addColorStop(0, '#0066ff');
      fillGrd.addColorStop(0.5, '#00b074');
      fillGrd.addColorStop(1, pct > 0.9 ? '#ffdd00' : '#00ffaa');
      ctx.fillStyle = fillGrd;
      ctx.shadowColor = pct > 0.9 ? '#ffcc00' : '#00ffaa';
      ctx.shadowBlur  = 10;
      ctx.beginPath();
      ctx.roundRect(gX, fillY, gW, fillH, 4);
      ctx.fill();
    }

    const markY = gY + gH - gH * 0.9;
    ctx.strokeStyle = 'rgba(255,200,0,0.7)';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([3, 3]);
    ctx.beginPath();
    ctx.moveTo(gX - 4, markY);
    ctx.lineTo(gX + gW + 4, markY);
    ctx.stroke();
    ctx.setLineDash([]);

    ctx.fillStyle = 'rgba(255,200,0,0.8)';
    ctx.font = '8px Courier New';
    ctx.textAlign = 'left';
    ctx.shadowBlur = 0;
    ctx.fillText('90%', gX + gW + 5, markY + 3);

    ctx.restore();
  }
}

/*CLASS: Game  (Vòng lặp chính)*/
class Game {
  constructor() {
    this.canvas  = document.getElementById('gameCanvas');
    this.ctx     = this.canvas.getContext('2d');
    this._resize();

    this.state   = 'playing';
    this.isPaused = true;     
    
    this.frame   = 0;
    this.scrollMult = 1;

    this.metrics    = new MedicalMetrics(CONFIG.TARGET_ROM);
    this.elbowAngle = 0;       
    this.displayAngle = 0;     

    this.player    = new Player(this.W, this.H);
    this.coins     = [];
    this.asteroids = [];
    this.bonusItems= [];
    this.particles = [];

    this.stars   = [];
    this.nebulas = [];
    for (let i = 0; i < CONFIG.STAR_COUNT; i++) {
      const layer = Math.floor(i / (CONFIG.STAR_COUNT / CONFIG.PARALLAX_LAYERS));
      this.stars.push(new Star(this.W, this.H, layer));
    }
    for (let i = 0; i < CONFIG.NEBULA_COUNT; i++) {
      this.nebulas.push(new Nebula(this.W, this.H));
    }

    this.hud = new HUD(this.W, this.H);

    this._coinTimer    = 0;
    this._asteroidTimer= 0;
    this._bonusTimer   = 600; 

    window.gameInstance = this;

    window.addEventListener('resize', () => this._resize());
    window.addEventListener('keydown', (e) => this._handleKey(e));

    this._loop();
  }

  _resize() {
    this.canvas.width  = this.W = window.innerWidth;
    this.canvas.height = this.H = window.innerHeight;
    if (this.player) {
      this.player.W = this.W;
      this.player.H = this.H;
    }
    if (this.hud) {
      this.hud.W = this.W;
      this.hud.H = this.H;
    }
  }

  receiveAngle(rawAngle) {
    this.elbowAngle = clamp(parseFloat(rawAngle) || 0, 0, CONFIG.TARGET_ROM);
    if (this.state !== 'playing' || this.isPaused) return;

    const repDone = this.metrics.update(this.elbowAngle);
    if (repDone) {
      this.hud.flashRep();
      this._spawnRepParticles();
    }
    this.player.setTargetFromAngle(this.elbowAngle);
  }

  _handleKey(e) {
    const step = 5;
    if (e.key === 'ArrowUp')   this.receiveAngle(Math.min(this.elbowAngle + step, 140));
    if (e.key === 'ArrowDown') this.receiveAngle(Math.max(this.elbowAngle - step, 0));
    if (e.key === 'r' || e.key === 'R') this.restartGame();
    if (e.key === 'p' || e.key === 'P') this.isPaused = !this.isPaused;
  }

  _spawnParticles(x, y, color, count = 12, type = 'explode') {
    for (let i = 0; i < count; i++) {
      this.particles.push(new Particle(x, y, color, type));
    }
  }

  _spawnRepParticles() {
    const cx = this.player.x;
    const cy = this.player.y;
    const colors = ['#00ffaa', '#00b074', '#ffffff', '#ffdd00'];
    for (let i = 0; i < 20; i++) {
      const c = colors[i % colors.length];
      this.particles.push(new Particle(cx, cy, c, 'collect'));
    }
  }

  _update() {
    if (this.state !== 'playing' || this.isPaused) return;
    
    this.frame++;
    this.displayAngle = lerp(this.displayAngle, this.elbowAngle, 0.12);

    this.scrollMult = Math.min(
      1 + (this.frame / 3600) * (CONFIG.SCROLL_SPEED_MAX / CONFIG.SCROLL_SPEED_BASE - 1),
      CONFIG.SCROLL_SPEED_MAX / CONFIG.SCROLL_SPEED_BASE
    );

    this.nebulas.forEach(n => n.update());
    this.stars.forEach(s => s.update());
    this.player.update();

    this._coinTimer++;
    const coinRate = Math.max(40, CONFIG.COIN_SPAWN_RATE - this.frame / 60);
    if (this._coinTimer >= coinRate) {
      this._coinTimer = 0;
      const count = randInt(1, 3);
      for (let i = 0; i < count; i++) {
        const coin = new Coin(this.W, this.H);
        coin.x += i * rand(40, 90);
        this.coins.push(coin);
      }
    }

    this._asteroidTimer++;
    const astRate = Math.max(60, CONFIG.ASTEROID_SPAWN_RATE - this.frame / 90);
    if (this._asteroidTimer >= astRate) {
      this._asteroidTimer = 0;
      this.asteroids.push(new Asteroid(this.W, this.H));
    }

    this._bonusTimer--;
    if (this._bonusTimer <= 0) {
      this._bonusTimer = 800 + randInt(0, 400);
      const peakPct = Math.min(this.metrics.peakRom / CONFIG.TARGET_ROM, 0.95);
      const margin  = 30;
      const byY = lerp(this.H - margin, margin, peakPct);
      this.bonusItems.push(new BonusItem(this.W, this.H, byY));
    }

    this.coins.forEach(c => c.update(this.scrollMult));
    this.asteroids.forEach(a => a.update(this.scrollMult));
    this.bonusItems.forEach(b => b.update(this.scrollMult));
    this.particles.forEach(p => p.update());
    this.particles = this.particles.filter(p => !p.dead);

    const hitCoins = CollisionManager.checkPlayer(
      this.player, this.coins, this.player.hitRadius
    );
    hitCoins.reverse().forEach(({ item, index }) => {
      this.player.score += item.value;
      this.hud.flashScore();
      this._spawnParticles(item.x, item.y,
        item.type === 'gold' ? '#ffd700' : item.type === 'blue' ? '#00aaff' : '#ffcc00',
        16, 'collect'
      );
      this.coins.splice(index, 1);
    });

    const hitAsts = CollisionManager.checkPlayer(
      this.player, this.asteroids, this.player.hitRadius
    );
    hitAsts.reverse().forEach(({ item, index }) => {
      const wasHit = this.player.hit();
      if (wasHit) {
        this._spawnParticles(item.x, item.y, '#ff5500', 24, 'explode');
        this._spawnParticles(this.player.x, this.player.y, '#4488ff', 12, 'explode');
        this.asteroids.splice(index, 1);
        if (this.player.health <= 0) {
          this._triggerGameOver();
        }
      }
    });

    const hitBonus = CollisionManager.checkPlayer(
      this.player, this.bonusItems, this.player.hitRadius
    );
    hitBonus.reverse().forEach(({ item, index }) => {
      this.player.score += item.value;
      this.hud.flashScore();
      if (this.player.health < CONFIG.MAX_HEALTH) this.player.health++;
      this._spawnParticles(item.x, item.y, '#ffdd00', 30, 'collect');
      this.bonusItems.splice(index, 1);
    });

    this.coins      = this.coins.filter(c => !c.dead);
    this.asteroids  = this.asteroids.filter(a => !a.dead);
    this.bonusItems = this.bonusItems.filter(b => !b.dead);
  }

  _triggerGameOver() {
    this.state = 'gameover';
    setTimeout(() => this._showSummary(), 800);
  }

  _showSummary() {
    const m = this.metrics;
    const overlay = document.getElementById('summaryOverlay');

    document.getElementById('res-score').textContent = this.player.score.toLocaleString();
    document.getElementById('res-reps').textContent  = m.reps;
    document.getElementById('res-peak').textContent  = `${Math.round(m.peakRom)}°`;
    document.getElementById('res-avg').textContent   = `${m.avgRom}°`;
    document.getElementById('res-time').textContent  = `${m.sessionSeconds}s`;
    document.getElementById('res-grade').textContent = m.grade;

    const pct = Math.min((m.peakRom / CONFIG.TARGET_ROM) * 100, 100);
    document.getElementById('res-rom-pct').textContent = `${Math.round(pct)}%`;
    setTimeout(() => {
      document.getElementById('res-rom-bar').style.width = `${pct}%`;
    }, 100);

    const targetPct = (CONFIG.TARGET_ROM / 140) * 100;
    document.getElementById('res-rom-target').style.left = `${targetPct}%`;

    overlay.classList.remove('hidden');
  }

  exportData() {
    const payload = {
      ...this.metrics.export(),
      score:      this.player.score,
      gameConfig: { targetRom: CONFIG.TARGET_ROM },
    };
    const json = JSON.stringify(payload);

    try {
      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(json);
      }
    } catch (e) {
      console.warn('[RehabTrack] WebView2 không khả dụng:', e);
    }

    const fb = document.getElementById('exportFeedback');
    fb.classList.remove('hidden');
    setTimeout(() => fb.classList.add('hidden'), 4000);

    return json; 
  }

  restartGame() {
    document.getElementById('summaryOverlay').classList.add('hidden');
    document.getElementById('exportFeedback').classList.add('hidden');

    this.state       = 'playing';
    this.isPaused    = false;
    this.frame       = 0;
    this.scrollMult  = 1;
    this.elbowAngle  = 0;
    this.displayAngle= 0;
    this.metrics     = new MedicalMetrics(CONFIG.TARGET_ROM);
    this.player      = new Player(this.W, this.H);
    this.coins       = [];
    this.asteroids   = [];
    this.bonusItems  = [];
    this.particles   = [];
    this._coinTimer  = 0;
    this._asteroidTimer = 0;
    this._bonusTimer = 600;
  }

  _draw() {
    const ctx = this.ctx;
    const W = this.W, H = this.H;

    ctx.fillStyle = '#020810';
    ctx.fillRect(0, 0, W, H);

    this.nebulas.forEach(n => n.draw(ctx));
    this.stars.forEach(s => s.draw(ctx));

    this.coins.forEach(c => c.draw(ctx));
    this.bonusItems.forEach(b => b.draw(ctx));
    this.asteroids.forEach(a => a.draw(ctx));

    if (this.state === 'playing' || this.state === 'dead') {
      this.player.draw(ctx);
    }

    this.particles.forEach(p => p.draw(ctx));

    if (this.state === 'playing') {
      this.hud.draw(
        ctx,
        this.metrics,
        this.player.score,
        this.player.health,
        this.scrollMult,
        this.displayAngle,
        CONFIG.TARGET_ROM
      );
    }

    if (this.state === 'gameover') {
      ctx.save();
      ctx.fillStyle = 'rgba(2,8,16,0.7)';
      ctx.fillRect(0, 0, W, H);
      ctx.font = 'bold 48px Courier New';
      ctx.fillStyle = '#ff4444';
      ctx.shadowColor = '#ff0000';
      ctx.shadowBlur  = 30;
      ctx.textAlign = 'center';
      ctx.fillText('GAME OVER', W / 2, H / 2 - 20);
      ctx.font = '18px Courier New';
      ctx.fillStyle = '#7a9e90';
      ctx.shadowBlur = 0;
      ctx.fillText('Đang tổng kết kết quả...', W / 2, H / 2 + 24);
      ctx.restore();
    }
    if (this.isPaused && this.state === 'playing') {
      ctx.save();
      ctx.fillStyle = 'rgba(2, 8, 16, 0.65)';
      ctx.fillRect(0, 0, W, H);
      ctx.font = 'bold 28px Courier New';
      ctx.fillStyle = '#ffcc00';
      ctx.textAlign = 'center';
      ctx.shadowColor = '#ffcc00';
      ctx.shadowBlur = 12;
      ctx.fillText('ĐANG CHỜ KẾT NỐI / TẠM DỪNG', W / 2, H / 2);
      
      ctx.font = '15px Courier New';
      ctx.fillStyle = '#7a9e90';
      ctx.shadowBlur = 0;
      ctx.fillText('Bấm "Kết nối Bluetooth" hoặc nút Tiếp tục trên phần mềm', W / 2, H / 2 + 35);
      ctx.restore();
    }

    this._drawAngleDebug(ctx);
  }

  _drawAngleDebug(ctx) {
    const cx = this.W - 52, cy = this.H - 52, r = 36;
    ctx.save();
    ctx.globalAlpha = 0.55;

    ctx.fillStyle = 'rgba(0,0,0,0.6)';
    ctx.beginPath();
    ctx.arc(cx, cy, r + 4, 0, Math.PI * 2);
    ctx.fill();

    ctx.strokeStyle = '#1a3a2a';
    ctx.lineWidth = 5;
    ctx.beginPath();
    ctx.arc(cx, cy, r, Math.PI, Math.PI * 2); 
    ctx.stroke();

    const angleFrac = clamp(this.displayAngle / CONFIG.TARGET_ROM, 0, 1);
    const startA = Math.PI;
    const endA   = Math.PI + angleFrac * Math.PI;
    const arcGrd = ctx.createLinearGradient(cx - r, cy, cx + r, cy);
    arcGrd.addColorStop(0, '#0044ff');
    arcGrd.addColorStop(0.5, '#00b074');
    arcGrd.addColorStop(1, '#ffdd00');
    ctx.strokeStyle = arcGrd;
    ctx.lineWidth = 5;
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.arc(cx, cy, r, startA, endA);
    ctx.stroke();

    const needleA = startA + angleFrac * Math.PI;
    ctx.strokeStyle = '#ffffff';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    ctx.lineTo(cx + Math.cos(needleA) * (r - 6), cy + Math.sin(needleA) * (r - 6));
    ctx.stroke();

    ctx.fillStyle = '#ffffff';
    ctx.beginPath();
    ctx.arc(cx, cy, 3, 0, Math.PI * 2);
    ctx.fill();

    ctx.globalAlpha = 1;
    ctx.font = '9px Courier New';
    ctx.fillStyle = '#7a9e90';
    ctx.textAlign = 'center';
    ctx.fillText(`${Math.round(this.displayAngle)}°`, cx, cy + r + 14);
    ctx.restore();
  }

  _loop() {
    this._update();
    this._draw();
    requestAnimationFrame(() => this._loop());
  }
}

/*API TOÀN CỤC – Giao tiếp với C# WPF WebView2 */

// 1. Cổng nhận dữ liệu Real-time (Tối ưu tốc độ cao)
if (window.chrome && window.chrome.webview) {
  window.chrome.webview.addEventListener('message', (event) => {
    // Nhận chuỗi góc thô từ C# PostWebMessageAsString
    if (window.gameInstance && !window.gameInstance.isPaused) {
      window.gameInstance.receiveAngle(event.data);
    }
  });
}

// 2. Fallback (Dự phòng cho ExecuteScriptAsync nếu C# vẫn dùng)
window.updateAngle = function(angle) {
  if (window.gameInstance) {
    window.gameInstance.receiveAngle(angle);
  }
};

window.exportData = function() {
  if (window.gameInstance) {
    window.gameInstance.exportData();
  }
};

window.startGame = function() {
  if (window.gameInstance) {
    window.gameInstance.isPaused = false;
    console.log("[JS] Game Resume/Start");
  }
};

window.pauseGame = function() {
  if (window.gameInstance) {
    window.gameInstance.isPaused = true;
    console.log("[JS] Game Paused");
  }
};

window.stopGame = function() {
  if (window.gameInstance) {
      window.gameInstance.isPaused = true;
      window.gameInstance._triggerGameOver();
      console.log("[JS] Game Stopped");
  }
}

/*KHỞI ĐỘNG */
window.addEventListener('DOMContentLoaded', () => {
  const loading = document.getElementById('loadingScreen');

  setTimeout(() => {
    // Nếu bạn có class .fade-out trong CSS, nó sẽ chạy mượt mà
    if(loading) {
       loading.classList.add('fade-out');
       setTimeout(() => {
         loading.style.display = 'none';
       }, 600);
    }
    
    // Khởi tạo Game Instance (Mặc định ở trạng thái isPaused = true)
    window.gameInstance = new Game();
    
    console.log("[JS] Game Engine Ready. Waiting for C# connection...");
  }, 1000); // Giảm thời gian loading xuống 1s cho nhanh
});