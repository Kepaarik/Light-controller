// Прошивка адресной ленты (WS2812B и т.п.) для управления из LedHub по USB.
// Требуется библиотека FastLED (Arduino IDE → Library Manager → FastLED).
//
// Протокол (строки, завершённые '\n', 115200 бод):
//   PING        -> ответ "LEDHUB <имя>", по нему LedHub находит плату
//   ON / OFF    -> включить / выключить ленту
//   C r g b     -> установить цвет (0–255 на канал)

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

void render() {
  fill_solid(leds, NUM_LEDS, isOn ? currentColor : CRGB::Black);
  FastLED.show();
}

void handleCommand(const String& cmd) {
  if (cmd == "PING") {
    Serial.print("LEDHUB ");
    Serial.println(DEVICE_NAME);
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

    if (c == '\n') {
      line.trim();
      handleCommand(line);
      line = "";
    } else if (line.length() < 32) {
      line += c;
    }
  }
}
