namespace KingLastPiece
{
    /// <summary>
    /// Level design of the normal mode (scene NormalMode): ten fixed rounds, souls-like — few pieces at a time so every
    /// round is about reading and dodging their attacks; some rounds send reinforcements as pieces are captured (another
    /// of the same, a rook, or the plain piece after its Dark form falls). Two queens are lone mid-bosses (round 5, and the
    /// Dark II queen in round 9) and the 체스메가트론 stands alone in the last round. Pieces enter on their real starting
    /// squares. (The endless mode generates its rounds instead — see <see cref="EndlessRounds"/>.)
    /// </summary>
    public static class ClassicRounds
    {
        /// <summary>What calls a reinforcement onto the board.</summary>
        public enum When
        {
            /// <summary>One of the round's own pieces (by its index in <see cref="Round.pieces"/>) is captured.</summary>
            Captured,
            /// <summary>The first of the round's own pieces to be captured, whichever it is.</summary>
            AnyCaptured,
            /// <summary>Every one of the round's own pieces of a type has been captured.</summary>
            AllOfTypeCaptured,
        }

        /// <summary>A piece that joins the round once, when its condition is met.</summary>
        public struct Reinforcement
        {
            public When when;
            public int piece;          // Captured: the index of the round's piece
            public PieceType ofType;   // AllOfTypeCaptured: the type
            public PieceType type;
            public string square;
            public int tier;
        }

        public struct Round
        {
            public string name;
            /// <summary>Piece, starting square and Dark tier (0 = a plain piece, 1–2 = Dark I–II with its aura).</summary>
            public (PieceType type, string square, int tier)[] pieces;
            /// <summary>Pieces called in as the round goes (each once); null: none.</summary>
            public Reinforcement[] reinforcements;
        }

        const PieceType P = PieceType.Pawn, N = PieceType.Knight, B = PieceType.Bishop, R = PieceType.Rook, Q = PieceType.Queen,
            M = PieceType.Megatron;

        /// <summary>Piece <paramref name="index"/> of the round captured: <paramref name="type"/> enters on <paramref name="square"/>.</summary>
        static Reinforcement OnCaptured(int index, PieceType type, string square, int tier = 0) =>
            new Reinforcement { when = When.Captured, piece = index, type = type, square = square, tier = tier };

        /// <summary>The first of the round's pieces captured: <paramref name="type"/> enters on <paramref name="square"/>.</summary>
        static Reinforcement OnFirstCapture(PieceType type, string square, int tier = 0) =>
            new Reinforcement { when = When.AnyCaptured, type = type, square = square, tier = tier };

        /// <summary>All of the round's <paramref name="of"/> captured: <paramref name="type"/> enters on <paramref name="square"/>.</summary>
        static Reinforcement OnAllCaptured(PieceType of, PieceType type, string square, int tier = 0) =>
            new Reinforcement { when = When.AllOfTypeCaptured, ofType = of, type = type, square = square, tier = tier };

        public static readonly Round[] All =
        {
            new Round { name = "I. 오프닝", pieces = new[] {
                (P, "d7", 0), (P, "e7", 0), (N, "g8", 0) } },
            // Bishop and knight mate: one of each, and each one captured brings in another of its kind.
            new Round { name = "II. 비나메이트", pieces = new[] {
                    (B, "c8", 0), (N, "g8", 0) },
                reinforcements = new[] { OnCaptured(0, B, "f8"), OnCaptured(1, N, "b8") } },
            // Doubled rooks: a rook and a bishop; the first capture brings the second rook.
            new Round { name = "III. 룩 더블링", pieces = new[] {
                    (R, "a8", 0), (B, "f8", 0) },
                reinforcements = new[] { OnFirstCapture(R, "h8") } },
            // Knights shrouded in darkness: a Dark I knight and rook, each coming back as a plain piece once captured.
            new Round { name = "IV. 어둠에 휩싸인 기사단", pieces = new[] {
                    (N, "g8", 1), (R, "a8", 1) },
                reinforcements = new[] { OnCaptured(0, N, "g8"), OnCaptured(1, R, "a8") } },
            // Mid-boss: the Black Queen alone (a pawn joins her at half health).
            new Round { name = "V. 블랙 퀸", pieces = new[] {
                (Q, "d8", 0) } },
            // Three bishops on the long diagonals: one Dark II, two Dark I.
            new Round { name = "VI. 피앙케토", pieces = new[] {
                (B, "b7", 2), (B, "g7", 1), (B, "c8", 1) } },
            // Black Lion Gambit: two knights and two pawns, Dark II and Dark I; both pawns captured, a Dark II rook enters.
            new Round { name = "VII. 블랙라이온 갬빗", pieces = new[] {
                    (N, "g8", 2), (N, "b8", 1), (P, "d7", 2), (P, "e7", 1) },
                reinforcements = new[] { OnAllCaptured(P, R, "h8", 2) } },
            // The King hunt: a Dark II rook, bishop, knight and pawn; each comes back as a plain piece once captured.
            new Round { name = "VIII. 킹 사냥", pieces = new[] {
                    (R, "h8", 2), (B, "c8", 2), (N, "g8", 2), (P, "e7", 2) },
                reinforcements = new[] { OnCaptured(0, R, "h8"), OnCaptured(1, B, "c8"), OnCaptured(2, N, "g8"), OnCaptured(3, P, "e7") } },
            // Mid-boss: the Dark II queen alone — checkmate lines, rains of squares, the purge.
            new Round { name = "IX. 분노의 어둠 여왕", pieces = new[] {
                (Q, "d8", 2) } },
            // Final round: the colossus of pawns, alone.
            new Round { name = "X. 체스 메가트론", pieces = new[] {
                (M, "e6", 0) } },
        };

        public static int Count => All.Length;
    }
}
