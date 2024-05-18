// This file is part of www.nand2tetris.org
// and the book "The Elements of Computing Systems"
// by Nisan and Schocken, MIT Press.
// File name: projects/04/Mult.asm

// Multiplies R0 and R1 and stores the result in R2.
// Assumes that R0 >= 0, R1 >= 0, and R0 * R1 < 32768.
// (R0, R1, R2 refer to RAM[0], RAM[1], and RAM[2], respectively.)

    // clean R[2] and @sum
    @R2
    M=0
    @sum
    M=0
    // read first argument
    @R0
    D=M
    // save it variable a
    @a
    M=D
    // prepare iterator
    @i
    M=0
(LOOP)
    // if i > b goto STOP
    @i
    D=M
    @R1
    D=D-M
    @STOP
    D;JGE
    // sum = sum + a
    @sum
    D=M
    @a
    D=D+M
    @sum
    M=D
    // i = i + 1
    @i
    M=M+1
    // goto LOOP
    @LOOP
    0;JMP
(STOP)
    // save @sum to R2
    @sum
    D=M
    @R2
    M=D
(END)
    @END
    0;JMP
