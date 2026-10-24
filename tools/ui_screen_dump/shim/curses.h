#pragma once

// A headless stand-in for curses, just the part src/ui_io.cpp uses (the only
// file in the game that calls curses), for tools/ui_screen_dump (M14). The
// screen is a 24x80 character buffer; keys come from a script. Its
// semantics are the ones the .NET port's Umoria.Core.Ui.Terminal
// implements, so the real ui_io.cpp / ui.cpp / ui_inventory.cpp code can be
// compared with the port's screen by screen:
//   - move() outside the screen fails (ERR) and leaves the cursor alone;
//   - addch() writes at the cursor and advances it, wrapping at the right
//     edge; the bottom-right cell fails (no scrolling);
//   - addch('\n') clears to the end of the line and moves to the next line,
//     addch('\t') blanks to the next multiple of 8;
//   - clrtoeol()/clrtobot() blank from the cursor without moving it;
//   - clear() blanks the screen and homes the cursor;
//   - overwrite() copies the characters (not the cursor);
//   - getch() in timeout (non-blocking) mode never reads a scripted key.
//
// This directory is put first on the harness's include path, so ui_io.cpp's
// #include "curses.h" finds it instead of PDCurses.

#define ERR (-1)
#define OK 0

struct ShimWindow;
typedef ShimWindow WINDOW;

extern WINDOW *stdscr;
extern WINDOW *curscr;
extern int LINES;
extern int COLS;

WINDOW *initscr();
WINDOW *newwin(int lines, int cols, int y, int x);
int endwin();

int raw();
int cbreak();
int noecho();
int nonl();
int keypad(WINDOW *win, bool enable);
int curs_set(int visibility);
int set_escdelay(int ms);
void timeout(int delay);

int clear();
int refresh();
int wrefresh(WINDOW *win);
int move(int y, int x);
int clrtoeol();
int clrtobot();
int addch(char ch);
int addstr(const char *str);
int mvaddch(int y, int x, char ch);
int mvaddstr(int y, int x, const char *str);
int mvcur(int oldrow, int oldcol, int newrow, int newcol);
int overwrite(WINDOW *src, WINDOW *dst);
int touchwin(WINDOW *win);
int getch();

void shimGetYX(WINDOW *win, int &y, int &x);
#define getyx(win, y, x) shimGetYX((win), (y), (x))

// ---- harness controls -----------------------------------------------------

#include <string>
#include <vector>

// The keys getch() returns, in order. When they run out getch() returns
// <ESCAPE> (and counts it), so a script that is too short ends its prompts
// the way the player would -- both sides do the same, and the dump shows it.
void shimSetKeys(const std::string &keys);
size_t shimKeysLeft();
int shimExtraEscapes();

// Called before each getch() reads a key (nullptr: nothing).
void shimSetBeforeKeyRead(void (*callback)());

// The screen as 24 lines of 80 characters, and the cursor.
std::vector<std::string> shimSnapshot();
void shimCursor(int &y, int &x);
