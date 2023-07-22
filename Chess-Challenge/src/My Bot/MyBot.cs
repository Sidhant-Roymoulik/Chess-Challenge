using ChessChallenge.API;
using System;

public class MyBot : IChessBot
{
    int CHECKMATE = 100000;

    int time_limit = 0;
    DateTime start = DateTime.Now;
    bool stop_search = false;

    public Move Think(Board board, Timer timer)
    {
        time_limit = timer.MillisecondsRemaining / 120;
        start = DateTime.Now;
        stop_search = false;


        Move[] moves = board.GetLegalMoves();
        Move best_move = moves[0];

        for (int depth = 1; depth < 100; depth++)
        {
            Move depth_move = moves[0];
            int score = Int32.MinValue;
            foreach (Move move in moves)
            {
                if (stop_search)
                    break;

                board.MakeMove(move);
                int new_score = -Search(board, depth - 1, 1);
                board.UndoMove(move);

                if (new_score > score)
                {
                    score = new_score;
                    depth_move = move;
                }
            }

            if (stop_search)
                break;

            best_move = depth_move;

            Console.WriteLine(String.Format("depth {0} time {1} score {2} pv {3}{4}",
                depth,
                (int)(DateTime.Now - start).TotalMilliseconds,
                score,
                best_move.StartSquare.Name, best_move.TargetSquare.Name));

            if (score > CHECKMATE / 2)
                break;
        }
        Console.WriteLine();

        return best_move;
    }

    public int Search(Board board, int depth, int ply)
    {
        if ((DateTime.Now - start).TotalMilliseconds > time_limit)
        {
            stop_search = true;
            return 0;
        }

        Move[] moves = board.GetLegalMoves();

        if (moves.Length == 0)
        {
            if (board.IsInCheckmate())
                return -CHECKMATE + ply;
            return 0;
        }

        if (depth <= 0)
            return Eval(board);


        int score = Int32.MinValue;
        foreach (Move move in moves)
        {
            board.MakeMove(move);
            int new_score = -Search(board, depth - 1, ply + 1);
            board.UndoMove(move);

            if (new_score > score)
            {
                score = new_score;
            }
        }

        return score;
    }

    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pvm_mg = { 0, 100, 320, 330, 500, 900, 20000 };

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

        return score[turn] - score[turn ^ 1];
    }
}