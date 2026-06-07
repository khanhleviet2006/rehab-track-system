/**
 * PHI THUYỀN KHÔNG GIAN - DYNAMIC ROM
 * Phục vụ tích hợp C# WPF WebView2
 */

// ==========================================
// 1. CÁC LỚP THỰC THỂ (ENTITIES)
// ==========================================

class Player {
    constructor(canvasHeight) {
        this.width = 60;
        this.height = 40;
        this.x = 100;
        this.y = canvasHeight / 2;
        this.targetY = canvasHeight / 2;
        this.color = '#00fcce';
        this.lerpSpeed = 0.08; // Nội suy mượt mà, chống giật do nhiễu cảm biến
    }

    update(targetY) {
        this.targetY = targetY;
        // Áp dụng công thức Lerp (Linear Interpolation)
        this.y += (this.targetY - this.y) * this.lerpSpeed;
    }

    draw(ctx) {
        ctx.fillStyle = this.color;
        ctx.beginPath();
        // Vẽ phi thuyền đơn giản (Hình tam giác ngang)
        ctx.moveTo(this.x, this.y - this.height / 2);
        ctx.lineTo(this.x + this.width, this.y);
        ctx.lineTo(this.x, this.y + this.height / 2);
        ctx.fill();
        
        // Hiệu ứng đuôi lửa
        ctx.fillStyle = '#ff5722';
        ctx.fillRect(this.x - 15, this.y - 5, 15 + Math.random() * 10, 10);
    }
}

class Coin {
    constructor(canvasWidth, canvasHeight) {
        this.radius = 15;
        this.x = canvasWidth + this.radius;
        this.y = Math.random() * (canvasHeight - 60) + 30;
        this.speed = 3 + Math.random() * 2;
        this.color = '#ffd700';
        this.markedForDeletion = false;
    }

    update() {
        this.x -= this.speed;
        if (this.x < -this.radius) this.markedForDeletion = true;
    }

    draw(ctx) {
        ctx.fillStyle = this.color;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.strokeStyle = '#fff';
        ctx.lineWidth = 2;
        ctx.stroke();
    }
}

class Asteroid {
    constructor(canvasWidth, canvasHeight) {
        this.radius = 20 + Math.random() * 15;
        this.x = canvasWidth + this.radius;
        this.y = Math.random() * (canvasHeight - 60) + 30;
        this.speed = 4 + Math.random() * 3;
        this.color = '#888';
        this.markedForDeletion = false;
    }

    update() {
        this.x -= this.speed;
        if (this.x < -this.radius) this.markedForDeletion = true;
    }

    draw(ctx) {
        ctx.fillStyle = this.color;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.radius, 0, Math.PI * 2);
        ctx.fill();
    }
}

class BonusItem {
    constructor(canvasWidth, targetY) {
        this.radius = 20;
        this.x = canvasWidth + this.radius;
        // Spawn chính xác tại góc mục tiêu (Peak ROM) để ép bệnh nhân gập tay
        this.y = targetY; 
        this.speed = 2.5;
        this.color = '#ff00ff';
        this.markedForDeletion = false;
    }

    update() {
        this.x -= this.speed;
        if (this.x < -this.radius) this.markedForDeletion = true;
    }

    draw(ctx) {
        ctx.fillStyle = this.color;
        ctx.shadowBlur = 15;
        ctx.shadowColor = this.color;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.radius, 0, Math.PI * 2);
        ctx.fill();
        ctx.shadowBlur = 0; // Reset shadow
    }
}

class Particle {
    constructor(x, y, color) {
        this.x = x;
        this.y = y;
        this.size = Math.random() * 5 + 2;
        this.speedX = Math.random() * 6 - 3;
        this.speedY = Math.random() * 6 - 3;
        this.color = color;
        this.life = 1.0;
    }
    update() {
        this.x += this.speedX;
        this.y += this.speedY;
        this.life -= 0.05;
    }
    draw(ctx) {
        ctx.fillStyle = `rgba(${this.color}, ${this.life})`;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size, 0, Math.PI * 2);
        ctx.fill();
    }
}

// ==========================================
// 2. CÁC LỚP QUẢN LÝ (MANAGERS)
// ==========================================

class CollisionManager {
    static check(rect1, circle2) {
        // AABB với hình tròn đơn giản hóa
        let distX = Math.abs(circle2.x - rect1.x - rect1.width / 2);
        let distY = Math.abs(circle2.y - rect1.y);

        if (distX > (rect1.width / 2 + circle2.radius)) return false;
        if (distY > (rect1.height / 2 + circle2.radius)) return false;

        if (distX <= (rect1.width / 2)) return true;
        if (distY <= (rect1.height / 2)) return true;

        let dx = distX - rect1.width / 2;
        let dy = distY - rect1.height / 2;
        return (dx * dx + dy * dy <= (circle2.radius * circle2.radius));
    }
}

class MedicalMetrics {
    constructor(targetROM) {
        this.targetROM = targetROM; // Góc gập mục tiêu bác sĩ giao (VD: 120)
        this.currentAngle = 0;
        this.minAngle = 140;
        this.maxAngle = 0;
        this.repCount = 0;
        this.angleHistory = []; // {time, angle}
        
        // Trạng thái đếm Reps
        this.repState = 'WAITING_FOR_FLEXION';
        this.sumPeakROM = 0; // Để tính Average Peak ROM
    }

    update(angle, timeStamp) {
        this.currentAngle = Math.max(0, Math.min(140, angle));
        this.minAngle = Math.min(this.minAngle, this.currentAngle);
        this.maxAngle = Math.max(this.maxAngle, this.currentAngle);
        
        this.angleHistory.push({ time: timeStamp, angle: this.currentAngle });

        // Logic đếm số lần lặp (Reps) cực kỳ quan trọng trong phục hồi chức năng
        let flexThreshold = this.targetROM * 0.9;
        let extThreshold = this.targetROM * 0.2;

        if (this.repState === 'WAITING_FOR_FLEXION' && this.currentAngle >= flexThreshold) {
            this.repState = 'WAITING_FOR_EXTENSION';
            // Trigger Particle hoặc âm thanh tại đây
        } 
        else if (this.repState === 'WAITING_FOR_EXTENSION' && this.currentAngle <= extThreshold) {
            this.repState = 'WAITING_FOR_FLEXION';
            this.repCount++;
            this.sumPeakROM += this.maxAngle; 
            // Reset max angle cho Rep tiếp theo
            this.maxAngle = this.currentAngle; 
        }
    }

    getPeakROM() {
        return this.maxAngle;
    }

    getAverageROM() {
        if (this.repCount === 0) return this.maxAngle;
        return (this.sumPeakROM / this.repCount).toFixed(1);
    }
}

class HUD {
    constructor(canvasWidth, canvasHeight) {
        this.width = canvasWidth;
        this.height = canvasHeight;
    }

    draw(ctx, game) {
        ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
        ctx.fillRect(0, 0, this.width, 50);

        ctx.fillStyle = 'white';
        ctx.font = '20px Arial';
        ctx.textAlign = 'left';
        ctx.fillText(`Điểm: ${game.score}`, 20, 32);
        ctx.fillText(`Máu: ${game.health}%`, 150, 32);
        
        ctx.fillStyle = '#00b074';
        ctx.fillText(`Reps: ${game.metrics.repCount}`, 300, 32);

        // Góc bên phải hiển thị thông số Y khoa
        ctx.textAlign = 'right';
        ctx.fillText(`Góc hiện tại: ${Math.round(game.metrics.currentAngle)}°`, this.width - 20, 32);
        
        // Vẽ thanh Progress / Lực bóp mỏng ở viền phải màn hình
        let barHeight = 200;
        let fillHeight = (game.metrics.currentAngle / 140) * barHeight;
        
        ctx.fillStyle = 'rgba(255,255,255,0.2)';
        ctx.fillRect(this.width - 30, this.height / 2 - 100, 10, barHeight);
        
        ctx.fillStyle = game.metrics.currentAngle > game.metrics.targetROM * 0.9 ? '#00b074' : '#00fcce';
        ctx.fillRect(this.width - 30, this.height / 2 + 100 - fillHeight, 10, fillHeight);
    }
}

class SessionManager {
    constructor() {
        this.startTime = Date.now();
        this.endTime = null;
    }

    endSession() {
        this.endTime = Date.now();
    }

    getDuration() {
        return Math.floor((this.endTime - this.startTime) / 1000); // Trả về giây
    }

    exportData(metrics, score) {
        return {
            sessionDate: new Date().toISOString(),
            durationSeconds: this.getDuration(),
            score: score,
            peakROM: metrics.getPeakROM(),
            averageROM: metrics.getAverageROM(),
            repetitions: metrics.repCount,
            targetROM: metrics.targetROM,
            angleHistory: metrics.angleHistory // JSON array gửi về C#
        };
    }
}

// ==========================================
// 3. LỚP ĐIỀU KHIỂN GAME (MAIN GAME LOOP)
// ==========================================

class Game {
    constructor(canvas) {
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.width = canvas.width;
        this.height = canvas.height;

        this.targetROM = 120; // Bác sĩ setup từ C# truyền xuống
        this.metrics = new MedicalMetrics(this.targetROM);
        this.session = new SessionManager();
        this.hud = new HUD(this.width, this.height);
        
        this.player = new Player(this.height);
        this.coins = [];
        this.asteroids = [];
        this.bonusItems = [];
        this.particles = [];
        
        this.score = 0;
        this.health = 100;
        this.isGameOver = false;

        this.lastTime = 0;
        this.itemTimer = 0;
        this.itemInterval = 1000;
    }

    // API lộ ra cho C# WPF gọi vào
    updateAngle(angle) {
        if (this.isGameOver) return;
        
        // Map góc (0 - 140) sang tọa độ Y của màn hình
        // Y=0 là đỉnh màn hình (140 độ), Y=height là đáy màn hình (0 độ)
        let safeAngle = Math.max(0, Math.min(140, angle));
        let targetY = this.height - ((safeAngle / 140) * this.height);
        
        // Ràng buộc không cho phi thuyền bay ra ngoài viền
        targetY = Math.max(this.player.height/2, Math.min(this.height - this.player.height/2, targetY));
        
        this.player.targetY = targetY;
        this.metrics.update(safeAngle, Date.now() - this.session.startTime);
    }

    spawnEntities(deltaTime) {
        if (this.itemTimer > this.itemInterval) {
            let rand = Math.random();
            if (rand < 0.5) {
                this.coins.push(new Coin(this.width, this.height));
            } else if (rand < 0.8) {
                this.asteroids.push(new Asteroid(this.width, this.height));
            } else {
                // Ép bệnh nhân gập tay lấy Bonus ở góc mục tiêu (Peak ROM)
                let bonusY = this.height - ((this.targetROM / 140) * this.height);
                this.bonusItems.push(new BonusItem(this.width, bonusY));
            }
            this.itemTimer = 0;
        } else {
            this.itemTimer += deltaTime;
        }
    }

    createParticles(x, y, colorRGB) {
        for (let i = 0; i < 15; i++) {
            this.particles.push(new Particle(x, y, colorRGB));
        }
    }

    update(deltaTime) {
        if (this.isGameOver) return;

        this.player.update(this.player.targetY);
        this.spawnEntities(deltaTime);

        // Update mảng Entities chung
        [...this.coins, ...this.asteroids, ...this.bonusItems, ...this.particles].forEach(obj => obj.update());

        // Kiểm tra va chạm Đồng xu
        this.coins.forEach(coin => {
            if (CollisionManager.check(this.player, coin)) {
                coin.markedForDeletion = true;
                this.score += 10;
                this.createParticles(coin.x, coin.y, '255, 215, 0');
            }
        });

        // Kiểm tra va chạm Thiên thạch
        this.asteroids.forEach(ast => {
            if (CollisionManager.check(this.player, ast)) {
                ast.markedForDeletion = true;
                this.health -= 20;
                this.createParticles(ast.x, ast.y, '255, 0, 0');
                if (this.health <= 0) this.endGame();
            }
        });

        // Kiểm tra va chạm Bonus ROM
        this.bonusItems.forEach(bonus => {
            if (CollisionManager.check(this.player, bonus)) {
                bonus.markedForDeletion = true;
                this.score += 50;
                this.createParticles(bonus.x, bonus.y, '255, 0, 255');
            }
        });

        // Xóa objects rác
        this.coins = this.coins.filter(c => !c.markedForDeletion);
        this.asteroids = this.asteroids.filter(a => !a.markedForDeletion);
        this.bonusItems = this.bonusItems.filter(b => !b.markedForDeletion);
        this.particles = this.particles.filter(p => p.life > 0);
    }

    draw() {
        this.ctx.clearRect(0, 0, this.width, this.height);
        
        // Vẽ background lưới tạo cảm giác không gian và đo lường
        this.ctx.strokeStyle = 'rgba(255,255,255,0.05)';
        for(let i=0; i<this.height; i+=50) {
            this.ctx.beginPath(); this.ctx.moveTo(0, i); this.ctx.lineTo(this.width, i); this.ctx.stroke();
        }

        [...this.coins, ...this.asteroids, ...this.bonusItems, ...this.particles].forEach(obj => obj.draw(this.ctx));
        this.player.draw(this.ctx);
        this.hud.draw(this.ctx, this);
    }

    loop(timeStamp) {
        let deltaTime = timeStamp - this.lastTime;
        this.lastTime = timeStamp;

        this.update(deltaTime);
        this.draw();

        if (!this.isGameOver) {
            requestAnimationFrame(this.loop.bind(this));
        }
    }

    endGame() {
        this.isGameOver = true;
        this.session.endSession();
        
        const finalData = this.session.exportData(this.metrics, this.score);
        
        // Hiển thị UI HTML tổng kết
        document.getElementById('final-score').innerText = finalData.score;
        document.getElementById('final-reps').innerText = finalData.repetitions;
        document.getElementById('final-peak-rom').innerText = `${finalData.peakROM}°`;
        document.getElementById('final-avg-rom').innerText = `${finalData.averageROM}°`;
        document.getElementById('end-screen').classList.remove('hidden');

        // Lắng nghe nút Export để bắn tín hiệu về C# WPF
        document.getElementById('btn-export').addEventListener('click', () => {
            const jsonData = JSON.stringify(finalData);
            console.log("Dữ liệu Y khoa xuất ra:", jsonData);
            
            // Nếu chạy trong WebView2 của C#, bắn message về Backend
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(jsonData);
            } else {
                alert("Đã xuất JSON ra Console log (Chế độ test trình duyệt)");
            }
        });
    }

    start() {
        this.lastTime = performance.now();
        requestAnimationFrame(this.loop.bind(this));
    }
}

// ==========================================
// 4. KHỞI TẠO VÀ RÀNG BUỘC KÍCH THƯỚC
// ==========================================

const canvas = document.getElementById('gameCanvas');
function resizeCanvas() {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
}
window.addEventListener('resize', resizeCanvas);
resizeCanvas();

// Khởi tạo Game
const game = new Game(canvas);
game.start();

// API toàn cục để C# WPF gọi qua hàm:
// webView.CoreWebView2.ExecuteScriptAsync($"updateElbowAngle({angle});");
window.updateElbowAngle = function(angle) {
    if (game) {
        game.updateAngle(angle);
    }
};

// --- Mã giả lập (Mock) cho quá trình Test trên Trình duyệt ---
// Bỏ comment đoạn dưới đây nếu muốn test thử không cần C# WPF
/*
let mockAngle = 0;
let mockDirection = 1;
setInterval(() => {
    mockAngle += mockDirection * 2;
    if(mockAngle >= 130) mockDirection = -1;
    if(mockAngle <= 10) mockDirection = 1;
    window.updateElbowAngle(mockAngle);
}, 30);
*/