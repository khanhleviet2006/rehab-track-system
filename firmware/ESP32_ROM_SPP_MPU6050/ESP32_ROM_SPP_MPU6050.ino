#include <Arduino.h>
#include <Wire.h>
#include <BluetoothSerial.h>
#include <math.h>

// ESP32-WROOM-32 / ESP32 Dev Module (Classic Bluetooth SPP).
// KHONG su dung voi ESP32-C3, ESP32-S3 (khong co Bluetooth Classic).
#if !defined(CONFIG_BT_ENABLED) || !defined(CONFIG_BLUEDROID_ENABLED) || !defined(CONFIG_BT_SPP_ENABLED)
#error "Board does not support Bluetooth Classic SPP."
#endif

static const int SDA_PIN = 21;
static const int SCL_PIN = 22;
static const uint32_t SEND_INTERVAL_MS = 50;
static const uint32_t SAMPLE_INTERVAL_US = 25000;
static const char* BT_NAME = "ESP32_ROM_SPP";

BluetoothSerial SerialBT;
uint8_t mpuAddr = 0;
float gyroBiasY = 0.0f;
float filteredAngle = 0.0f;
float zeroAngle = 0.0f;
uint32_t lastSampleUs = 0;
uint32_t lastSendMs = 0;
uint32_t lastHelloMs = 0;
uint32_t lastRetryMs = 0;
int readFailures = 0;
bool mpuReady = false;

struct Sample {
  int16_t ax, ay, az, gx, gy, gz;
};

float wrap180(float x) {
  while (x > 180.0f) x -= 360.0f;
  while (x < -180.0f) x += 360.0f;
  return x;
}

bool writeReg(uint8_t reg, uint8_t val) {
  Wire.beginTransmission(mpuAddr);
  Wire.write(reg);
  Wire.write(val);
  return Wire.endTransmission() == 0;
}

bool readReg(uint8_t reg, uint8_t& val) {
  Wire.beginTransmission(mpuAddr);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(mpuAddr, (uint8_t)1, true) != 1) return false;
  val = Wire.read();
  return true;
}

bool readSample(Sample& s) {
  if (mpuAddr == 0) return false;
  Wire.beginTransmission(mpuAddr);
  Wire.write(0x3B);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(mpuAddr, (uint8_t)14, true) != 14) return false;

  s.ax = (int16_t)((Wire.read() << 8) | Wire.read());
  s.ay = (int16_t)((Wire.read() << 8) | Wire.read());
  s.az = (int16_t)((Wire.read() << 8) | Wire.read());
  Wire.read(); Wire.read(); // Temperature
  s.gx = (int16_t)((Wire.read() << 8) | Wire.read());
  s.gy = (int16_t)((Wire.read() << 8) | Wire.read());
  s.gz = (int16_t)((Wire.read() << 8) | Wire.read());
  return true;
}

bool initMpu() {
  mpuReady = false;
  mpuAddr = 0;
  for (uint8_t addr : { (uint8_t)0x68, (uint8_t)0x69 }) {
    Wire.beginTransmission(addr);
    if (Wire.endTransmission() == 0) {
      mpuAddr = addr;
      break;
    }
  }
  if (!mpuAddr) {
    Serial.println("#ERROR: MPU6050 not found at 0x68/0x69");
    return false;
  }
  uint8_t who = 0;
  if (!readReg(0x75, who)) {
    Serial.println("#ERROR: Cannot read WHO_AM_I");
    return false;
  }
  Serial.printf("#I2C=0x%02X WHO_AM_I=0x%02X\n", mpuAddr, who);
  // Fail closed: do not transmit uncertain clinical angle data.
  if (who != 0x68) {
    Serial.println("#ERROR: WHO_AM_I is not MPU6050 (0x68). Check the actual sensor IC.");
    return false;
  }
  if (!writeReg(0x6B, 0x00)) return false; // Wake up
  delay(100);
  if (!writeReg(0x1A, 0x03)) return false; // Filter
  if (!writeReg(0x1B, 0x00)) return false; // Gyro +/-250 deg/s
  if (!writeReg(0x1C, 0x00)) return false; // Acc +/-2g

  // Keep the device stationary, in the intended zero position.
  float sumX = 0.0f, sumZ = 0.0f, sumGy = 0.0f;
  int n = 0;
  for (int i = 0; i < 120; ++i) {
    Sample s;
    if (readSample(s)) {
      sumX += s.ax;
      sumZ += s.az;
      sumGy += s.gy;
      n++;
    }
    delay(10);
  }
  if (n < 100 || fabsf(sumX) + fabsf(sumZ) < 1000.0f) {
    Serial.println("#ERROR: MPU6050 calibration failed");
    return false;
  }

  gyroBiasY = sumGy / n;
  filteredAngle = atan2f(sumX, sumZ) * RAD_TO_DEG;
  zeroAngle = filteredAngle;
  lastSampleUs = micros();
  lastSendMs = millis();
  readFailures = 0;
  mpuReady = true;
  Serial.println("#SENSOR_READY");
  return true;
}

void setup() {
  Serial.begin(115200);
  Wire.begin(SDA_PIN, SCL_PIN);
  Wire.setClock(100000);
  Wire.setTimeOut(30);
  if (!SerialBT.begin(BT_NAME)) {
    Serial.println("#ERROR: Bluetooth Classic SPP initialization failed");
  }
  mpuReady = initMpu();
}

void loop() {
  // Optional Bluetooth terminal command "Z": use current orientation as zero.
  while (SerialBT.available()) {
    char c = SerialBT.read();
    if (c == 'Z' || c == 'z') {
      zeroAngle = filteredAngle;
      SerialBT.println("#ZERO_OK");
    }
  }

  uint32_t ms = millis();
  if (!mpuReady) {
    if ((uint32_t)(ms - lastHelloMs) >= 1000) {
      lastHelloMs = ms;
      if (SerialBT.hasClient()) SerialBT.println("#SENSOR_ERROR");
    }
    if ((uint32_t)(ms - lastRetryMs) >= 2000) {
      lastRetryMs = ms;
      mpuReady = initMpu();
    }
    delay(5);
    return;
  }

  if (SerialBT.hasClient() && (uint32_t)(ms - lastHelloMs) >= 1000) {
    lastHelloMs = ms;
    SerialBT.println("#ESP32_ROM_SPP");
  }

  uint32_t us = micros();
  if ((uint32_t)(us - lastSampleUs) < SAMPLE_INTERVAL_US) {
    delay(1);
    return;
  }
  float dt = (uint32_t)(us - lastSampleUs) / 1000000.0f;
  lastSampleUs = us;
  if (dt <= 0.0f || dt > 0.2f) dt = 0.025f;

  Sample s;
  if (!readSample(s)) {
    if (++readFailures >= 5) {
      mpuReady = false;
      Serial.println("#ERROR: I2C read failed");
    }
    return;
  }
  readFailures = 0;

  float predicted = wrap180(filteredAngle -
    (((float)s.gy - gyroBiasY) / 131.0f) * dt);
  float accAngle = atan2f((float)s.ax, (float)s.az) * RAD_TO_DEG;
  float norm = sqrtf((float)s.ax * s.ax +
                     (float)s.ay * s.ay +
                     (float)s.az * s.az) / 16384.0f;
  filteredAngle = (norm > 0.7f && norm < 1.3f)
    ? wrap180(predicted + 0.04f * wrap180(accAngle - predicted))
    : predicted;

  if ((uint32_t)(ms - lastSendMs) >= SEND_INTERVAL_MS) {
    lastSendMs = ms;
    float angle = fabsf(wrap180(filteredAngle - zeroAngle));
    if (isfinite(angle)) {
      // WPF expects a single invariant-culture number per line.
      if (SerialBT.hasClient()) SerialBT.println(angle, 1);
      Serial.println(angle, 1); // USB test output
    }
  }
}
