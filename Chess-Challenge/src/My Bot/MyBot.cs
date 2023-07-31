// #define UCI

#define TESTING
// #define SLOW

using ChessChallenge.API;
using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

public class MyBot : IChessBot
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
    const ulong TT_ENTRIES = 0x8FFFFF;
    Entry[] tt = new Entry[TT_ENTRIES];

    // thanks for the compressed pst implementation https://github.com/JacquesRW
    readonly ulong[] pst_compressed = { 657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421, 366006826859316436, 366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430, 402634039558223453, 347425219986941203, 365698755348489557, 311382605788951956, 147850316371514514, 329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759, 291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181, 402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804, 347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780, 365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492, 347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863, 419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674, 311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 402607438610388375, 329978099633296596, 67159620133902 };
    int[,,] pst = new int[2, 6, 64];

    // Constructor for precomputation
    public MyBot()
    {
        // Pre-extract all values from compressed pst
        for (int phase = 0; phase < 2; phase++)
            for (int piece = 0; piece < 6; piece++)
                for (int sq = 0; sq < 64; sq++)
                {
                    // Get index in compressed pst
                    int ind = 128 * piece + 64 * phase + sq;
                    // Populate pst using decompression
                    pst[phase, piece, sq] = (int)(((pst_compressed[ind / 10] >> (6 * (ind % 10))) & 63) - 20) * 8;
                }
    }

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
        if (!q_search && moves.Length == 0) { return board.IsInCheck() ? -CHECKMATE + ply : 0; }

        // Determine type of node cutoff
        int flag = best_score >= beta ? BETA_FLAG : best_score > start_alpha ? EXACT_FLAG : ALPHA_FLAG;
        // Save position to transposition table
        tt[key % TT_ENTRIES] = new Entry(key, best_score, depth, flag, best_move);

        return best_score;
    }

    // PeSTO Evaluation Function
    readonly int[] pvm_mg = { 82, 337, 365, 477, 1025, 10000 };
    readonly int[] pvm_eg = { 94, 281, 297, 512, 936, 10000 };
    readonly int[] phase_weight = { 0, 1, 1, 2, 4, 0 };

    // TODO: King Safety
    // TODO: Pawn Structure
    // TODO: Mobility
    public int Eval()
    {
        // Define evaluation variables
        int mg = 0, eg = 0, phase = 0;
        // Iterate through both players
        foreach (bool stm in new[] { true, false })
        {
            // Iterate through all piece types
            for (int piece = 0; piece < 6; piece++)
            {
                // Get piece bitboard
                ulong bb = board.GetPieceBitboard((PieceType)(piece + 1), stm);

                // Iterate through each individual piece
                while (bb != 0)
                {
                    // Get square index for pst based on color
                    int sq = BitboardHelper.ClearAndGetIndexOfLSB(ref bb) ^ (stm ? 56 : 0);
                    // Increment mg and eg score
                    mg += pvm_mg[piece] + pst[0, piece, sq];
                    eg += pvm_eg[piece] + pst[1, piece, sq];
                    // Updating position phase
                    phase += phase_weight[piece];
                }
            }
            mg = -mg;
            eg = -eg;
        }

        // In case of premature promotion
        phase = Math.Min(phase, 24);
        // Tapered evaluation
        return (mg * phase + eg * (24 - phase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }
}