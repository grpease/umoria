// The headless curses stand-in for tools/ui_screen_dump -- see shim/curses.h.

#include "shim/curses.h"

#include <cstdio>
#include <cstdlib>

constexpr int ROWS = 24;
constexpr int COLUMNS = 80;

struct ShimWindow {
    char cells[ROWS][COLUMNS];
    int y;
    int x;
};

static ShimWindow screen_window;
static ShimWindow spare_window;
static ShimWindow current_window; // curscr -- unused, but must exist

WINDOW *stdscr = &screen_window;
WINDOW *curscr = &current_window;
int LINES = ROWS;
int COLS = COLUMNS;

static std::string script;
static size_t script_pos = 0;
static int extra_escapes = 0;
static int input_delay = -1;
static void (*before_key_read)() = nullptr;

static void blank(ShimWindow &win) {
    for (auto &row : win.cells) {
        for (auto &cell : row) {
            cell = ' ';
        }
    }
    win.y = 0;
    win.x = 0;
}

WINDOW *initscr() {
    blank(screen_window);
    blank(current_window);
    return stdscr;
}

WINDOW *newwin(int, int, int, int) {
    blank(spare_window);
    return &spare_window;
}

int endwin() {
    return OK;
}

int raw() {
    return OK;
}

int cbreak() {
    return OK;
}

int noecho() {
    return OK;
}

int nonl() {
    return OK;
}

int keypad(WINDOW *, bool) {
    return OK;
}

int curs_set(int) {
    return OK;
}

int set_escdelay(int) {
    return OK;
}

void timeout(int delay) {
    input_delay = delay;
}

int clear() {
    blank(screen_window);
    return OK;
}

int refresh() {
    return OK;
}

int wrefresh(WINDOW *) {
    return OK;
}

int move(int y, int x) {
    if (y < 0 || y >= ROWS || x < 0 || x >= COLUMNS) {
        return ERR;
    }
    screen_window.y = y;
    screen_window.x = x;
    return OK;
}

int clrtoeol() {
    for (int x = screen_window.x; x < COLUMNS; x++) {
        screen_window.cells[screen_window.y][x] = ' ';
    }
    return OK;
}

int clrtobot() {
    clrtoeol();
    for (int y = screen_window.y + 1; y < ROWS; y++) {
        for (int x = 0; x < COLUMNS; x++) {
            screen_window.cells[y][x] = ' ';
        }
    }
    return OK;
}

int addch(char ch) {
    ShimWindow &w = screen_window;

    if (ch == '\n') {
        clrtoeol();
        if (w.y >= ROWS - 1) {
            return ERR;
        }
        w.y++;
        w.x = 0;
        return OK;
    }

    if (ch == '\t') {
        do {
            if (addch(' ') == ERR) {
                return ERR;
            }
        } while (w.x % 8 != 0);
        return OK;
    }

    w.cells[w.y][w.x] = ch;

    if (w.x < COLUMNS - 1) {
        w.x++;
    } else if (w.y < ROWS - 1) {
        w.x = 0;
        w.y++;
    } else {
        return ERR;
    }

    return OK;
}

int addstr(const char *str) {
    for (; *str != '\0'; str++) {
        if (addch(*str) == ERR) {
            return ERR;
        }
    }
    return OK;
}

int mvaddch(int y, int x, char ch) {
    if (move(y, x) == ERR) {
        return ERR;
    }
    return addch(ch);
}

int mvaddstr(int y, int x, const char *str) {
    if (move(y, x) == ERR) {
        return ERR;
    }
    return addstr(str);
}

int mvcur(int, int, int, int) {
    return OK;
}

int overwrite(WINDOW *src, WINDOW *dst) {
    for (int y = 0; y < ROWS; y++) {
        for (int x = 0; x < COLUMNS; x++) {
            dst->cells[y][x] = src->cells[y][x];
        }
    }
    return OK;
}

int touchwin(WINDOW *) {
    return OK;
}

int getch() {
    // non-blocking reads (flushInputBuffer(), checkForNonBlockingKeyPress())
    // find nothing typed ahead
    if (input_delay >= 0) {
        return ERR;
    }

    if (before_key_read != nullptr) {
        before_key_read();
    }

    if (script_pos < script.size()) {
        return (unsigned char) script[script_pos++];
    }

    extra_escapes++;
    if (extra_escapes > 50) {
        std::fprintf(stderr, "ui_screen_dump: a prompt keeps reading past the end of its script\n");
        std::exit(4);
    }
    return 27;
}

void shimGetYX(WINDOW *win, int &y, int &x) {
    y = win->y;
    x = win->x;
}

void shimSetKeys(const std::string &keys) {
    script = keys;
    script_pos = 0;
    extra_escapes = 0;
}

size_t shimKeysLeft() {
    return script.size() - script_pos;
}

int shimExtraEscapes() {
    return extra_escapes;
}

void shimSetBeforeKeyRead(void (*callback)()) {
    before_key_read = callback;
}

std::vector<std::string> shimSnapshot() {
    std::vector<std::string> lines;
    for (auto const &row : screen_window.cells) {
        lines.emplace_back(row, COLUMNS);
    }
    return lines;
}

void shimCursor(int &y, int &x) {
    y = screen_window.y;
    x = screen_window.x;
}
