using ChessChallenge.API;
using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    int CHECKMATE = 100000;
    int time_limit = 0;
    DateTime start = DateTime.Now;
    Move depth_move = new Move();
    Int64 nodes = 0;

    public Move Think(Board board, Timer timer)
    {
        return Iterative_Deepening(board, timer);
    }

    public Move Iterative_Deepening(Board board, Timer timer)
    {
        time_limit = timer.MillisecondsRemaining / 120;
        start = DateTime.Now;
        nodes = 0;

        Move[] moves = board.GetLegalMoves();
        Move best_move = moves[0];

        for (int depth = 1; depth < 100; depth++)
        {
            depth_move = moves[0];
            int score = Search(board, depth, 0, -CHECKMATE, CHECKMATE);

            if ((DateTime.Now - start).TotalMilliseconds > time_limit)
                break;

            best_move = depth_move;

            Console.WriteLine(String.Format("depth {0} score {1} nodes {2} nps {3} time {4} pv {5}{6}",
                depth,
                score,
                nodes,
                (Int64)(1000 * nodes / (DateTime.Now - start).TotalMilliseconds),
                (int)(DateTime.Now - start).TotalMilliseconds,
                best_move.StartSquare.Name,
                best_move.TargetSquare.Name));

            if (score > CHECKMATE / 2)
                break;
        }
        Console.WriteLine();

        return best_move;
    }

    public int Search(Board board, int depth, int ply, int alpha, int beta)
    {
        nodes++;

        if ((DateTime.Now - start).TotalMilliseconds > time_limit)
            return 0;

        if (board.IsInCheckmate())
            return -CHECKMATE + ply;

        if (board.IsDraw())
            return 0;

        if (depth <= 0)
            return Q_Search(board, ply, 0, alpha, beta);

        System.Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int new_score = -Search(board, depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);

            if (new_score > alpha)
            {
                if (ply == 0)
                    depth_move = move;

                if (new_score >= beta)
                    return beta;

                alpha = new_score;
            }
        }

        return alpha;
    }

    public int Q_Search(Board board, int depth, int ply, int alpha, int beta)
    {
        nodes++;

        if ((DateTime.Now - start).TotalMilliseconds > time_limit)
            return 0;

        if (board.IsInCheckmate())
            return -CHECKMATE + ply;

        if (board.IsDraw())
            return 0;

        if (depth <= 0)
            return Eval(board);

        // Delta Pruning
        int eval = Eval(board);

        if (eval >= beta)
            return beta;

        if (eval > alpha)
            alpha = eval;

        System.Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves, true);
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int new_score = -Q_Search(board, depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);

            if (new_score > alpha)
            {
                if (new_score >= beta)
                    return beta;

                alpha = new_score;
            }
        }

        return alpha;
    }

    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pvm_mg = { 0, 82, 337, 365, 477, 1025, 20000 };

    int[] mg_king_table = {
        -65,  23,  16, -15, -56, -34,   2,  13,
        29,  -1, -20,  -7,  -8,  -4, -38, -29,
        -9,  24,   2, -16, -20,   6,  22, -22,
        -17, -20, -12, -27, -30, -25, -14, -36,
        -49,  -1, -27, -39, -46, -44, -33, -51,
        -14, -14, -22, -46, -44, -30, -15, -27,
        1,   7,  -8, -64, -43, -16,   9,   8,
        -15,  36,  12, -54,   8, -28,  24,  14,
    };

    public int Flip(Square sq, int color)
    {
        if (color == 1)
            return sq.Index ^ 56;
        return sq.Index;
    }

    public int Eval(Board board)
    {
        int turn = Convert.ToInt32(board.IsWhiteToMove);
        int[] score = { 0, 0 };

        PieceList[] all_pieces = board.GetAllPieceLists();

        foreach (PieceList piece_list in all_pieces)
        {
            int color = Convert.ToInt32(piece_list.IsWhitePieceList);
            PieceType type = piece_list.TypeOfPieceInList;

            score[color] += pvm_mg[(int)type] * piece_list.Count;
        }

        score[0] += mg_king_table[Flip(board.GetKingSquare(false), 0)];
        score[1] += mg_king_table[Flip(board.GetKingSquare(true), 1)];

        return score[turn] - score[turn ^ 1];
    }
}