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

                if (score > CHECKMATE / 2)
                    break;
            }

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
                return Eval(board);

            Move[] moves = board.GetLegalMoves();
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