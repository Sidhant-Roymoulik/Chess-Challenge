using ChessChallenge.API;
using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    int CHECKMATE = 100000;
    static Board board;
    Timer timer;
    int time_limit = 0;
    DateTime start = DateTime.Now;
    Move depth_move = new Move();
    Int64 nodes = 0;

    public Move Think(Board board_input, Timer timer_input)
    {
        board = board_input;
        timer = timer_input;
        return Iterative_Deepening();
    }

    public Move Iterative_Deepening()
    {
        time_limit = timer.MillisecondsRemaining / 40;
        start = DateTime.Now;
        nodes = 0;

        Move[] moves = board.GetLegalMoves();
        Move best_move = moves[0];

        for (int depth = 1; depth < 100; depth++)
        {
            depth_move = moves[0];
            int score = Negamax(depth, 0, -CHECKMATE, CHECKMATE);

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

    public int Negamax(int depth, int ply, int alpha, int beta)
    {
        nodes++;

        if ((DateTime.Now - start).TotalMilliseconds > time_limit) return 0;
        if (board.IsInCheckmate()) return -CHECKMATE + ply;
        if (board.IsDraw()) return 0;
        if (depth <= 0) return Q_Search(ply, 0, alpha, beta);

        System.Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves);
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int new_score = -Negamax(depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);

            if (new_score > alpha)
            {
                if (ply == 0) depth_move = move;
                if (new_score >= beta) return beta;
                alpha = new_score;
            }
        }

        return alpha;
    }

    public int Q_Search(int depth, int ply, int alpha, int beta)
    {
        nodes++;

        if ((DateTime.Now - start).TotalMilliseconds > time_limit) return 0;
        if (board.IsInCheckmate()) return -CHECKMATE + ply;
        if (board.IsDraw()) return 0;
        if (depth <= 0) return Eval();

        // Delta Pruning
        int eval = Eval();
        if (eval >= beta) return beta;
        if (eval > alpha) alpha = eval;

        System.Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves, true);
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int new_score = -Q_Search(depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);

            if (new_score >= beta) return beta;
            alpha = Math.Max(alpha, new_score);
        }

        return alpha;
    }

    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pvm_mg = { 0, 100, 320, 330, 500, 1000, 20000 };
    int[] pvm_eg = { 0, 100, 320, 330, 500, 1000, 20000 };

    // thanks for the pst implementation https://github.com/iudwgerte
    static int[] edge_dist = { 0, 1, 2, 3, 3, 2, 1, 0 };

    // functions that attempt to simulate a piece square table
    private static Func<Square, int>[] pst_mg = {
        sq => 0,                                                                //null
        sq => sq.Rank*10-10+(sq.Rank==1&&edge_dist[sq.File]!=3?40:0)+(edge_dist[sq.Rank]==3&&edge_dist[sq.File]==3?10:0),   //pawn
        sq => (edge_dist[sq.Rank]+edge_dist[sq.File])*10,                       //knight
        sq => pst_mg[2](sq),                                                    //bishop
        sq => sq.Rank==6?10:0+((sq.Rank==0&&edge_dist[sq.File]==3)?10:0),       //rook
        sq => (edge_dist[sq.Rank]+edge_dist[sq.File])*5,                        //queen
        sq => (3-edge_dist[sq.Rank]+3-edge_dist[sq.File])*10-5-(sq.Rank>1?50:0) //king
    };
    private static Func<Square, int>[] pst_eg = {
        sq => 0,                                            //null
        sq => sq.Rank*20,                                   //pawn
        sq => pst_mg[2](sq),                                //knight
        sq => pst_mg[2](sq),                                //bishop
        sq => pst_mg[5](sq),                                //rook
        sq => pst_mg[5](sq),                                //queen
        sq => pst_mg[5](sq)                                 //king
    };

    static int[] phase_weight = { 0, 0, 1, 1, 2, 4, 0 };

    public int Eval()
    {
        int turn = Convert.ToInt32(board.IsWhiteToMove);
        int[] score_mg = { 0, 0 };
        int[] score_eg = { 0, 0 };
        int phase = 0;

        for (int piece_type = 1; piece_type <= 6; piece_type++)
        {
            ulong white_bb = board.GetPieceBitboard((PieceType)piece_type, true);
            ulong black_bb = board.GetPieceBitboard((PieceType)piece_type, false);

            while (white_bb > 0)
            {
                Square sq = new Square(BitboardHelper.ClearAndGetIndexOfLSB(ref white_bb));
                score_mg[1] += pvm_mg[piece_type] + pst_mg[piece_type](sq);
                score_eg[1] += pvm_eg[piece_type] + pst_eg[piece_type](sq);
                phase += phase_weight[piece_type];
            }
            while (black_bb > 0)
            {
                Square sq = new Square(BitboardHelper.ClearAndGetIndexOfLSB(ref black_bb) ^ 56);
                score_mg[0] += pvm_mg[piece_type] + pst_mg[piece_type](sq);
                score_eg[0] += pvm_eg[piece_type] + pst_eg[piece_type](sq);
                phase += phase_weight[piece_type];
            }
        }

        return ((score_mg[turn] - score_mg[turn ^ 1]) * phase + (score_eg[turn] - score_eg[turn ^ 1]) * (24 - phase)) / 24;
    }
}