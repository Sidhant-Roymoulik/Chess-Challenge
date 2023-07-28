using ChessChallenge.API;
using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;

public class MyBot : IChessBot
{
    // Define globals to save tokens
    int CHECKMATE = 100000;
    Board board;
    Timer timer;
    int time_limit = 0;
    Move depth_move = Move.NullMove;
    Int64 nodes = 0;

    // Types of Nodes
    int ALPHA_FLAG = 0, EXACT_FLAG = 1, BETA_FLAG = 2;
    // TT Entry Definition
    struct Entry
    {
        public ulong key;
        public int score, depth, flag;
        public Move move;
        public Entry(ulong _key, int _score, int _depth, int _flag, Move _move)
        {
            key = _key; score = _score; depth = _depth; flag = _flag; move = _move;
        }
    }
    // TT Definition
    const ulong TT_ENTRIES = 0x8FFFFF;
    Entry[] tt;

    // thanks for the compressed pst implementation https://github.com/JacquesRW
    readonly ulong[] pst_compressed = { 657614902731556116, 420894446315227099, 384592972471695068, 312245244820264086, 364876803783607569, 366006824779723922, 366006826859316500, 786039115310605588, 421220596516513823, 366011295806342421, 366006826859316436, 366006896669578452, 162218943720801556, 440575073001255824, 657087419459913430, 402634039558223453, 347425219986941203, 365698755348489557, 311382605788951956, 147850316371514514, 329107007234708689, 402598430990222677, 402611905376114006, 329415149680141460, 257053881053295759, 291134268204721362, 492947507967247313, 367159395376767958, 384021229732455700, 384307098409076181, 402035762391246293, 328847661003244824, 365712019230110867, 366002427738801364, 384307168185238804, 347996828560606484, 329692156834174227, 365439338182165780, 386018218798040211, 456959123538409047, 347157285952386452, 365711880701965780, 365997890021704981, 221896035722130452, 384289231362147538, 384307167128540502, 366006826859320596, 366006826876093716, 366002360093332756, 366006824694793492, 347992428333053139, 457508666683233428, 329723156783776785, 329401687190893908, 366002356855326100, 366288301819245844, 329978030930875600, 420621693221156179, 422042614449657239, 384602117564867863, 419505151144195476, 366274972473194070, 329406075454444949, 275354286769374224, 366855645423297932, 329991151972070674, 311105941360174354, 256772197720318995, 365993560693875923, 258219435335676691, 383730812414424149, 384601907111998612, 401758895947998613, 420612834953622999, 402607438610388375, 329978099633296596, 67159620133902 };
    int[,,] pst;

    // Constructor for precomputation
    public MyBot()
    {
        tt = new Entry[TT_ENTRIES];
        pst = new int[2, 6, 64];

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
        nodes = 0;
        time_limit = timer.MillisecondsRemaining / 2000;
        return Iterative_Deepening();
    }

    public Move Iterative_Deepening()
    {
        Move best_move = Move.NullMove;

        // Iterative Deepening Loop
        for (int depth = 1; depth < 100; depth++)
        {
            int score = Negamax(depth, 0, -CHECKMATE, CHECKMATE);

            // Check if time is expired
            if (timer.MillisecondsElapsedThisTurn > time_limit)
                break;

            best_move = depth_move;

            // UCI Debug Logging
            // Console.WriteLine(String.Format("depth {0} score {1} nodes {2} nps {3} time {4} pv {5}{6}",
            //     depth,
            //     score,
            //     nodes,
            //     (Int64)(1000 * nodes / (timer.MillisecondsElapsedThisTurn + 1)),
            //     timer.MillisecondsElapsedThisTurn,
            //     best_move.StartSquare.Name,
            //     best_move.TargetSquare.Name
            // ));

            // If a checkmate is found, exit search early to save time
            if (score > CHECKMATE / 2)
                break;
        }
        // Console.WriteLine();

        return best_move;
    }

    public int Negamax(int depth, int ply, int alpha, int beta)
    {
        // Increment node counter
        nodes++;

        // Define search variables
        bool root = ply == 0;
        bool q_search = depth <= 0;
        int best_score = -CHECKMATE;
        ulong key = board.ZobristKey;
        Move tt_move = Move.NullMove;

        // Check if time is expired
        if (timer.MillisecondsElapsedThisTurn > time_limit) return 0;
        // Check for draw by repetition
        if (!root && board.IsRepeatedPosition()) return -20;

        // TT Pruning
        Entry tt_entry = tt[key % TT_ENTRIES];
        if (tt_entry.key == key)
        {
            tt_move = tt_entry.move;
            if (!root && tt_entry.depth >= depth && (
                    (tt_entry.flag == ALPHA_FLAG && tt_entry.score <= alpha) ||
                    tt_entry.flag == EXACT_FLAG ||
                    (tt_entry.flag == BETA_FLAG && tt_entry.score >= beta)
                )
            )
                return tt_entry.score;
        }

        // Delta Pruning
        if (q_search)
        {
            best_score = Eval();
            if (best_score >= beta) return beta;
            if (best_score < alpha - 1025) return alpha;
            alpha = Math.Max(alpha, best_score);
        }

        Move[] moves = board.GetLegalMoves(q_search);

        // Move Ordering
        (Move, int)[] scored_moves = new (Move, int)[moves.Length];
        for (int i = 0; i < moves.Length; i++)
            scored_moves[i] = (moves[i], Score_Move(moves[i], tt_move));

        int start_alpha = alpha;
        for (int i = 0; i < moves.Length; i++)
        {
            // Sort moves in one-iteration bubble sort
            for (int j = i + 1; j < moves.Length; j++)
                if (scored_moves[i].Item2 < scored_moves[j].Item2)
                    (scored_moves[i], scored_moves[j]) = (scored_moves[j], scored_moves[i]);

            Move move = scored_moves[i].Item1;
            board.MakeMove(move);
            int new_score = -Negamax(depth - 1, ply + 1, -beta, -alpha);
            board.UndoMove(move);

            if (new_score > best_score)
            {
                best_score = new_score;
                tt_move = move;

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
        tt[key % TT_ENTRIES] = new Entry(key, best_score, depth, flag, tt_move);

        return best_score;
    }

    // PeSTO Evaluation Function
    readonly int[] pvm_mg = { 82, 337, 365, 477, 1025, 20000 };
    readonly int[] pvm_eg = { 94, 281, 297, 512, 936, 20000 };
    readonly int[] phase_weight = { 0, 1, 1, 2, 4, 0 };

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

    // Score moves using TT and MVV-LVA
    public int Score_Move(Move move, Move tt_move)
    {
        // TT-Move
        if (move == tt_move)
            return 100;
        // MVV-LVA
        if (move.IsCapture)
            return 10 * (int)move.CapturePieceType - (int)move.MovePieceType;
        return 0;
    }
}