// #define UCI
// #define SLOW

using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
    // Define globals to save tokens
    private Board board;
    private Timer timer;
    private int time_limit;
    private Move best_move_root;
    private int[,,] history_table;
    private int gamephase;
    // Move[] killer_moves = new Move[128];
    private readonly int[] move_scores = new int[256];

    // TT Definition
    private readonly (ulong, int, Move, int, int)[] tt = new (ulong, int, Move, int, int)[0x400000];

#if UCI
    private long nodes;
#endif

    // Required Think Method
    public Move Think(Board _board, Timer _timer)
    {
        board = _board;
        timer = _timer;
        time_limit = timer.MillisecondsRemaining / 30;
        history_table = new int[2, 7, 64];
#if SLOW
        time_limit = timer.MillisecondsRemaining / 1;
#endif
#if UCI
        nodes = 0;
#endif
        // Iterative Deepening Loop
        for (int depth = 1, alpha = -100000, beta = 100000; ;)
        {
            int score = Negamax(depth, 0, alpha, beta, true);

            // Check if time is expired
            if (timer.MillisecondsElapsedThisTurn > time_limit)
                break;

            if (score <= alpha) alpha -= 100;
            else if (score >= beta) beta += 100;
            else
            {
#if UCI
                string score_string = score.ToString();
                if (score > 50000)
                {
                    int pliesToMate = 99999 - score;
                    int mateInN = (pliesToMate / 2) + (pliesToMate % 2);
                    score_string = "mate " + mateInN;
                }
                if (score < -50000)
                {
                    int pliesToMate = -99999 - score;
                    int mateInN = (pliesToMate / 2) + (pliesToMate % 2);
                    score_string = "mate " + mateInN;
                }


                // UCI Debug Logging
                Console.WriteLine("info depth {0,2} score {1,7} nodes {2,9} nps {3,8} time {4,5} pv {5}{6}",
                    depth,
                    score_string,
                    nodes,
                    1000 * nodes / (timer.MillisecondsElapsedThisTurn + 1),
                    timer.MillisecondsElapsedThisTurn,
                    best_move_root.StartSquare.Name,
                    best_move_root.TargetSquare.Name
                );
#endif
                alpha = score - 20;
                beta = score + 20;
                depth++;
            }
        }
#if UCI
        Console.WriteLine();
#endif

        return best_move_root;
    }

    private int Negamax(int depth, int ply, int alpha, int beta, bool do_null)
    {
        // Increment node counter
#if UCI
        nodes++;
#endif
        // Define search variables
        bool root = ply++ == 0,
            in_check = board.IsInCheck(),
            pv_node = beta - alpha > 1;

        // Check for draw by repetition
        if (!root && board.IsRepeatedPosition()) return 0;

        if (in_check) depth++;

        ulong key = board.ZobristKey;
        ref var tt_entry = ref tt[key & 0x3FFFFF];
        int best_score = -200000,
            moves_scored = 0,
            tt_entry_score = tt_entry.Item2,
            tt_entry_flag = tt_entry.Item5;

        // TT Pruning
        if (tt_entry.Item1 == key && !root && tt_entry.Item4 >= depth && (
                tt_entry_flag == 1 ||
                (tt_entry_flag == 0 && tt_entry_score <= alpha) ||
                (tt_entry_flag == 2 && tt_entry_score >= beta)))
            return tt_entry_score;

        bool q_search = depth <= 0,
            can_futility_prune = false;

        // Delta Pruning
        if (q_search)
        {
            best_score = Eval();
            if (best_score >= beta) return best_score;
            alpha = Math.Max(alpha, best_score);
        }
        else if (!pv_node && !in_check)
        {
            // Static eval calculation for pruning
            int static_eval = Eval();

            // Reverse Futility Pruning
            if (depth < 7 && static_eval - 109 * depth >= beta) return static_eval;
            // Null Move Pruning
            if (do_null && depth >= 2 && gamephase > 0)
            {
                board.TrySkipTurn();
                int score = -Negamax(depth - 3 - depth / 4, ply, -beta, -alpha, false);
                board.UndoSkipTurn();
                if (score >= beta) return score;
            }
            // Futility Pruning Check
            can_futility_prune = depth < 6 && static_eval + 94 * depth <= alpha;
        }

        // Fix stack overflow issue
        if (ply > 100) return Eval();

        // Generate appropriate moves depending on whether we're in QSearch
        Span<Move> moves = stackalloc Move[256];
        board.GetLegalMovesNonAlloc(ref moves, q_search && !in_check);

        // Order moves in reverse order -> negative values are ordered higher hence the flipped values
        foreach (Move move in moves)
            move_scores[moves_scored++] = -(
                // Hash move
                move == tt_entry.Item3 ? 10_000_000 :
                // MVV-LVA
                move.IsCapture ? 1_000_000 * (int)move.CapturePieceType - (int)move.MovePieceType :
                // Promotion
                move.IsPromotion ? 8_000_000 :
                // Killer Moves
                // move == killer_moves[ply] ? 900_000 :
                // History Heuristic
                history_table[ply & 1, (int)move.MovePieceType, move.TargetSquare.Index]
            );

        move_scores.AsSpan(0, moves.Length).Sort(moves);

        // If there are no moves return either checkmate or draw
        if (!q_search && moves.Length == 0) return in_check ? ply - 100000 : 0;

        Move best_move = default;
        int start_alpha = alpha, i = 0, new_score;

        // Using local method to simplify multiple similar calls to Negamax
        int Search(int next_alpha, int R = 1) => new_score = -Negamax(depth - R, ply, -next_alpha, -alpha, do_null);

        foreach (Move move in moves)
        {
            // Futility Pruning
            if (can_futility_prune && !(move.IsCapture || move.IsPromotion) && i > 0) continue;

            board.MakeMove(move);
            // PVS + LMR (Saves tokens, I will not explain, ask Tyrant)
            if (i == 0 || q_search)
                Search(beta);
            else if ((i < 6 || depth < 3 ?
                        new_score = alpha + 1 :
                        Search(alpha + 1, 3)) > alpha &&
                    Search(alpha + 1) > alpha)
                Search(beta);
            board.UndoMove(move);

            if (new_score > best_score)
            {
                best_score = new_score;
                best_move = move;

                // Update bestmove
                if (root) best_move_root = move;
                // Improve alpha
                alpha = Math.Max(alpha, best_score);
                // Beta Cutoff
                if (alpha >= beta)
                {
                    if (!q_search && !move.IsCapture)
                        // {
                        history_table[ply & 1, (int)move.MovePieceType, move.TargetSquare.Index] += depth * depth;
                    // killer_moves[ply] = best_move;
                    // }
                    break;
                }
            }

            // Check if time is expired
            if (timer.MillisecondsElapsedThisTurn > time_limit) return 200000;
            i++;
        }

        // Save position to transposition table
        tt[key & 0x3FFFFF] = (
            key,
            best_score,
            best_move,
            depth,
            best_score >= beta ? 2 : best_score > start_alpha ? 1 : 0
        );

        return best_score;
    }

    // PeSTO Evaluation Function
    private readonly int[] phase_weight = { 0, 1, 1, 2, 4, 0 };
    // thanks for the compressed pst implementation Tyrant
    // None, Pawn, Knight, Bishop, Rook, Queen, King 
    private readonly short[] pvm = { 82, 337, 365, 477, 1025, 20000, // Middlegame
                                     94, 281, 297, 512, 936, 20000}; // Endgame
    private readonly int[][] UnpackedPestoTables;

    // TODO: King Safety
    // TODO: Pawn Structure
    // TODO: Mobility
    private int Eval()
    {
        int middlegame = 0, endgame = 0, sideToMove = 2, piece, square;
        gamephase = 0;
        for (; --sideToMove >= 0; middlegame = -middlegame, endgame = -endgame)
            for (piece = -1; ++piece < 6;)
                for (ulong mask = board.GetPieceBitboard((PieceType)piece + 1, sideToMove > 0); mask != 0;)
                {
                    // Gamephase, middlegame -> endgame
                    gamephase += phase_weight[piece];

                    // Material and square evaluation
                    square = BitboardHelper.ClearAndGetIndexOfLSB(ref mask) ^ 56 * sideToMove;
                    middlegame += UnpackedPestoTables[square][piece];
                    endgame += UnpackedPestoTables[square][piece + 6];
                }
        return (middlegame * gamephase + endgame * (24 - gamephase)) / 24 * (board.IsWhiteToMove ? 1 : -1);
    }

    public MyBot()
    {
        // Precompute PSTs
        UnpackedPestoTables = new[] {
            63746705523041458768562654720m, 71818693703096985528394040064m, 75532537544690978830456252672m, 75536154932036771593352371712m, 76774085526445040292133284352m, 3110608541636285947269332480m, 936945638387574698250991104m, 75531285965747665584902616832m,
            77047302762000299964198997571m, 3730792265775293618620982364m, 3121489077029470166123295018m, 3747712412930601838683035969m, 3763381335243474116535455791m, 8067176012614548496052660822m, 4977175895537975520060507415m, 2475894077091727551177487608m,
            2458978764687427073924784380m, 3718684080556872886692423941m, 4959037324412353051075877138m, 3135972447545098299460234261m, 4371494653131335197311645996m, 9624249097030609585804826662m, 9301461106541282841985626641m, 2793818196182115168911564530m,
            77683174186957799541255830262m, 4660418590176711545920359433m, 4971145620211324499469864196m, 5608211711321183125202150414m, 5617883191736004891949734160m, 7150801075091790966455611144m, 5619082524459738931006868492m, 649197923531967450704711664m,
            75809334407291469990832437230m, 78322691297526401047122740223m, 4348529951871323093202439165m, 4990460191572192980035045640m, 5597312470813537077508379404m, 4980755617409140165251173636m, 1890741055734852330174483975m, 76772801025035254361275759599m,
            75502243563200070682362835182m, 78896921543467230670583692029m, 2489164206166677455700101373m, 4338830174078735659125311481m, 4960199192571758553533648130m, 3420013420025511569771334658m, 1557077491473974933188251927m, 77376040767919248347203368440m,
            73949978050619586491881614568m, 77043619187199676893167803647m, 1212557245150259869494540530m, 3081561358716686153294085872m, 3392217589357453836837847030m, 1219782446916489227407330320m, 78580145051212187267589731866m, 75798434925965430405537592305m,
            68369566912511282590874449920m, 72396532057599326246617936384m, 75186737388538008131054524416m, 77027917484951889231108827392m, 73655004947793353634062267392m, 76417372019396591550492896512m, 74568981255592060493492515584m, 70529879645288096380279255040m,
        }.Select(packedTable =>
            {
                int pieceType = 0;
                return new System.Numerics.BigInteger(packedTable)
                    .ToByteArray()
                    .Take(12)
                    .Select(square => (int)((sbyte)square * 1.461) + pvm[pieceType++])
                    .ToArray();
            }
        ).ToArray();
    }
}