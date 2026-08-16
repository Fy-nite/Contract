#include <stdint.h>

typedef struct { unsigned char r, g, b, a; } Color;
typedef struct { float x, y, z; } Vec3;
typedef struct { Vec3 origin; float w, h; } Rect;

unsigned char color_max(Color c) {
    unsigned char m = c.r;
    if (c.g > m) m = c.g;
    if (c.b > m) m = c.b;
    return m;
}

Color color_make(unsigned char r, unsigned char g, unsigned char b, unsigned char a) {
    Color c = { r, g, b, a };
    return c;
}

uint32_t sum_u(uint32_t a, uint32_t b) { return a + b; }
uint16_t sum_us(uint16_t a, uint16_t b) { return (uint16_t)(a + b); }
int16_t diff_s(int16_t a, int16_t b) { return (int16_t)(a - b); }

float vec_dot(Vec3 a, Vec3 b) {
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

float rect_width(Rect r) { return r.w; }

Rect rect_make(float x, float y, float z, float w, float h) {
    Rect r;
    r.origin.x = x; r.origin.y = y; r.origin.z = z;
    r.w = w; r.h = h;
    return r;
}
