// This file is part of www.nand2tetris.org
// and the book "The Elements of Computing Systems"
// by Nisan and Schocken, MIT Press.
// File name: projects/04/Fill.asm

// Runs an infinite loop that listens to the keyboard input.
// When a key is pressed (any key), the program blackens the screen
// by writing 'black' in every pixel;
// the screen should remain fully black as long as the key is pressed. 
// When no key is pressed, the program clears the screen by writing
// 'white' in every pixel;
// the screen should remain fully clear as long as no key is pressed.

    // iterating over the screen
    @i
    M=0
    // key pressed or not
    @state
    M=0
(LOOP)
    @KBD
    D=M
    @NOKEY
    D;JEQ
(PRESS)
    // blacken sixteen pixel starting at ith register of SCREEN
    @i
    D=M
    @SCREEN
    A=D+A
    M=-1
    // bump i by one
    @i
    M=M+1
    // jump back to the start of the loop
    @LOOP
    0;JMP
(NOKEY)
    // whiten sixteen pixel starting at ith register of SCREEN
    @i
    D=M
    @SCREEN
    A=D+A
    M=0
    // cut i by one
    @i
    M=M-1
    @LOOP
    0;JMP
