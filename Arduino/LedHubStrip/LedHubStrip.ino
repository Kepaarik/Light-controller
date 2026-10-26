// Прошивка адресной ленты (WS2812B и т.п.) для управления из LedHub по USB.
// Требуется библиотека FastLED (Arduino IDE → Library Manager → FastLED).
//
// Протокол (строки, завершённые '\n', 115200 бод):
//   PING        -> ответ "LEDHUB <имя>", по нему LedHub находит плату
//   ON / OFF    -> включить / выключить ленту
//   C r g b     -> установить цвет (0–255 на канал)
//   COUNT       -> ответ "LEDS <число светодиодов>"
//   F RRGGBB... -> цвет каждого светодиода по порядку, шестнадцатеричными парами

#include <FastLED.h>

// Имя должно быть уникальным для каждой платы: по нему LedHub запоминает ленту в группах
#define DEVICE_NAME "Arduino Strip 1"

#define DATA_PIN    6
#define NUM_LEDS    60
#define LED_TYPE    WS2812B
#define COLOR_ORDER GRB

CRGB leds[NUM_LEDS];
CRGB currentColor = CRGB::White;
bool isOn = false;
String line;

// Кадр слишком длинный для буфера строки, поэтому разбирается прямо по мере приёма
bool isReadingFrame = false;
int frameByte = 0;
int highNibble = -1;

int hexValue(char c) {
  if (c >= '0' && c <= '9') return c - '0';
  if (c >= 'A' && c <= 'F') return c - 'A' + 10;
  if (c >= 'a' && c <= 'f') return c - 'a' + 10;
  return -1;
}

void readFrameChar(char c) {
  if (c == '\n') {
    isReadingFrame = false;
    isOn = true;
    FastLED.show();
    return;
  }

  int value = hexValue(c);
  if (value < 0) {
    return;
  }

  if (highNibble < 0) {
    highNibble = value;
    return;
  }

  // CRGB хранит каналы подряд как r, g, b — пишем байты кадра прямо в массив светодиодов
  if (frameByte < NUM_LEDS * 3) {
    ((uint8_t*)leds)[frameByte++] = highNibble * 16 + value;
  }
  highNibble = -1;
}

void render() {
  fill_solid(leds, NUM_LEDS, isOn ? currentColor : CRGB::Black);
  FastLED.show();
}

void handleCommand(const String& cmd) {
  if (cmd == "PING") {
    Serial.print("LEDHUB ");
    Serial.println(DEVICE_NAME);
  } else if (cmd == "COUNT") {
    Serial.print("LEDS ");
    Serial.println(NUM_LEDS);
  } else if (cmd == "ON") {
    isOn = true;
    render();
  } else if (cmd == "OFF") {
    isOn = false;
    render();
  } else if (cmd.startsWith("C ")) {
    int r, g, b;
    if (sscanf(cmd.c_str() + 2, "%d %d %d", &r, &g, &b) == 3) {
      currentColor = CRGB(r, g, b);
      render();
    }
  }
}

void setup() {
  Serial.begin(115200);
  FastLED.addLeds<LED_TYPE, DATA_PIN, COLOR_ORDER>(leds, NUM_LEDS);
  render();
}

void loop() {
  while (Serial.available()) {
    char c = Serial.read();

    if (isReadingFrame) {
      readFrameChar(c);
      continue;
    }

    // Ни одна другая команда не начинается с 'F'
    if (line.length() == 0 && c == 'F') {
      isReadingFrame = true;
      frameByte = 0;
      highNibble = -1;
      continue;
    }

    if (c == '\n') {
      line.trim();
      handleCommand(line);
      line = "";
    } else if (line.length() < 32) {
      line += c;
    }
  }
}
