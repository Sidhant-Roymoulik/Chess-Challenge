// #define UCI

// #define TESTING
// #define SLOW

using ChessChallenge.API;
using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ChessChallenge.Example
{
    public class EvilBot : IChessBot
    {
        // Define globals to save tokens
        readonly int CHECKMATE = 100000;
        Board board;
        Timer timer;
        int time_limit;
        Move depth_move;

#if UCI
    long nodes;
#endif

        // Types of Nodes
        readonly int ALPHA_FLAG = 0, EXACT_FLAG = 1, BETA_FLAG = 2;
        // TT Entry Definition
        record struct Entry(ulong Key, int Score, int Depth, int Flag, Move Move);
        // TT Definition
        const ulong TT_ENTRIES = 0x7FFFFF;
        Entry[] tt = new Entry[TT_ENTRIES];

        // Required Think Method
        public Move Think(Board _board, Timer _timer)
        {
            board = _board;
            timer = _timer;
            time_limit = timer.MillisecondsRemaining / 40;
#if SLOW
        time_limit = timer.MillisecondsRemaining / 2;
#endif
#if TESTING
        time_limit = timer.MillisecondsRemaining / 2000;
#endif
#if UCI
        nodes = 0;
#endif
            Move best_move = Move.NullMove;
            // Iterative Deepening Loop
            for (int depth = 1; depth < 100; depth++)
            {
                int score = Negamax(depth, 0, -CHECKMATE, CHECKMATE);

                // Check if time is expired
                if (timer.MillisecondsElapsedThisTurn > time_limit)
                    break;

                best_move = depth_move;
#if UCI
            // UCI Debug Logging
            Console.WriteLine("depth {0,2} score {1,6} nodes {2,9} nps {3,8} time {4,5} pv {5}{6}",
                depth,
                score,
                nodes,
                1000 * nodes / (timer.MillisecondsElapsedThisTurn + 1),
                timer.MillisecondsElapsedThisTurn,
                best_move.StartSquare.Name,
                best_move.TargetSquare.Name
            );
#endif

                // If a checkmate is found, exit search early to save time
                if (score > CHECKMATE / 2)
                    break;
            }
#if UCI
        Console.WriteLine();
#endif

            return best_move;
        }

        public int Negamax(int depth, int ply, int alpha, int beta)
        {
            // Increment node counter
#if UCI
        nodes++;
#endif
            // Define search variables
            bool root = ply == 0;
            bool q_search = depth <= 0;
            int best_score = -CHECKMATE;
            ulong key = board.ZobristKey;

            // Check for draw by repetition
            if (!root && board.IsRepeatedPosition()) return -20;

            // TT Pruning
            Entry tt_entry = tt[key % TT_ENTRIES];
            if (tt_entry.Key == key && !root && tt_entry.Depth >= depth && (
                    tt_entry.Flag == EXACT_FLAG ||
                    (tt_entry.Flag == ALPHA_FLAG && tt_entry.Score <= alpha) ||
                    (tt_entry.Flag == BETA_FLAG && tt_entry.Score >= beta)))
                return tt_entry.Score;

            // Delta Pruning
            if (q_search)
            {
                best_score = Eval();
                if (best_score >= beta) return beta;
                alpha = Math.Max(alpha, best_score);
            }

            Move[] moves;
            if (board.IsInCheck()) moves = board.GetLegalMoves();
            else moves = board.GetLegalMoves(q_search);
            // Move Ordering
            int[] move_scores = new int[moves.Length];
            for (int i = 0; i < moves.Length; i++)
            {
                // TT-Move + MVV-LVA
                move_scores[i] = moves[i] == tt_entry.Move ? 100000 :
                moves[i].IsCapture ? 100 * (int)moves[i].CapturePieceType - (int)moves[i].MovePieceType :
                moves[i].IsPromotion ? (int)moves[i].PromotionPieceType : 0;
            }

            Move best_move = Move.NullMove;
            int start_alpha = alpha;
            for (int i = 0; i < moves.Length; i++)
            {
                // Check if time is expired
                if (timer.MillisecondsElapsedThisTurn > time_limit) return 0;

                // Sort moves in one-iteration bubble sort
                for (int j = i + 1; j < moves.Length; j++)
                    if (move_scores[i] < move_scores[j])
                        (moves[i], moves[j], move_scores[i], move_scores[j]) =
                        (moves[j], moves[i], move_scores[j], move_scores[i]);

                Move move = moves[i];
                board.MakeMove(move);
                int new_score;
                if (i == 0 || q_search)
                    // Principal-variation search
                    new_score = -Negamax(depth - 1, ply + 1, -beta, -alpha);
                else
                {
                    // Null-window search
                    new_score = -Negamax(depth - 1, ply + 1, -alpha - 1, -alpha);
                    if (new_score > alpha)
                        // Principal-variation search
                        new_score = -Negamax(depth - 1, ply + 1, -beta, -new_score);
                }
                board.UndoMove(move);

                if (new_score > best_score)
                {
                    best_score = new_score;
                    best_move = move;

                    // Update bestmove
                    if (root) depth_move = move;
                    // Improve alpha
                    alpha = Math.Max(alpha, best_score);
                    // Beta Cutoff
                    if (alpha >= beta) break;
                }
            }

            // If there are no moves return either checkmate or draw
            if (!q_search && moves.Length == 0) return board.IsInCheck() ? -CHECKMATE + ply : 0;

            // Determine type of node cutoff
            int flag = best_score >= beta ? BETA_FLAG : best_score > start_alpha ? EXACT_FLAG : ALPHA_FLAG;
            // Save position to transposition table
            tt[key % TT_ENTRIES] = new Entry(key, best_score, depth, flag, best_move);

            return best_score;
        }

        // None, Pawn, Knight, Bishop, Rook, Queen, King 
        private readonly short[] PieceValues = { 82, 337, 365, 477, 1025, 0, // Middlegame
                                             94, 281, 297, 512, 936, 0}; // Endgame

        // Big table packed with data from premade piece square tables
        // Unpack using PackedEvaluationTables[set, rank] = file
        private readonly decimal[] PackedPestoTables = {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        };

        readonly int[] phase_weight = { 0, 1, 1, 2, 4, 0 };

        // TODO: King Safety
        // TODO: Pawn Structure
        // TODO: Mobility

        private int Eval()
        {
            int middlegame = 0, endgame = 0, gamephase = 0;
            foreach (PieceList list in board.GetAllPieceLists())
                foreach (Piece piece in list)
                {
                    int pieceType = (int)list.TypeOfPieceInList - 1;
                    int colour = list.IsWhitePieceList ? 1 : -1;
                    int index = piece.Square.Index ^ (piece.IsWhite ? 56 : 0);

                    middlegame += colour * UnpackedPestoTables[index][pieceType];
                    endgame += colour * UnpackedPestoTables[index][pieceType + 6];
                    gamephase += phase_weight[pieceType];
                }

            // Tapered evaluation
            int middlegamePhase = Math.Min(gamephase, 24);
            return (middlegame * middlegamePhase + endgame * (24 - middlegamePhase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
        }

        private readonly int[][] UnpackedPestoTables;
        public EvilBot()
        {
            UnpackedPestoTables = new int[64][];
            for (int i = 0; i < 64; i++)
            {
                int pieceType = 0;
                UnpackedPestoTables[i] = decimal.GetBits(PackedPestoTables[i]).Take(3)
                    .SelectMany(c => BitConverter.GetBytes(c)
                        .Select((byte square) => (int)((sbyte)square * 1.461) + PieceValues[pieceType++]))
                    .ToArray();
            }
        }
    }
}