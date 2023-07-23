using ChessChallenge.API;
using System;

namespace ChessChallenge.Example
{
    // A simple bot that can spot mate in one, and always captures the most valuable piece it can.
    // Plays randomly otherwise.
    public class EvilBot : IChessBot
    {
        int CHECKMATE = 100000;

        int time_limit = 0;
        DateTime start = DateTime.Now;
        bool stop_search = false;
        Int64 nodes = 0;

        public Move Think(Board board, Timer timer)
        {
            time_limit = timer.MillisecondsRemaining / 120;
            start = DateTime.Now;
            stop_search = false;
            nodes = 0;

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

                    nodes++;

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

                if (score > CHECKMATE / 2)
                    break;
            }

            return best_move;
        }

        public int Search(Board board, int depth, int ply)
        {
            if ((DateTime.Now - start).TotalMilliseconds > time_limit)
            {
                stop_search = true;
                return 0;
            }

            nodes++;

            Move[] moves = board.GetLegalMoves();

            if (board.IsInCheckmate())
                return -CHECKMATE + ply;

            if (board.IsDraw())
                return 0;

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
}