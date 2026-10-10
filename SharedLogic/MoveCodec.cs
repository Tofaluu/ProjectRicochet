using System;
using System.Collections.Generic;

namespace SharedLogic
{
    /// <summary>Wire format for a move list: one byte per move, robot in bits 2-3 and direction in bits 0-1.</summary>
    public static class MoveCodec
    {
        public static byte[] Encode(IReadOnlyList<Move> moves)
        {
            var data = new byte[moves.Count];
            for (int i = 0; i < moves.Count; i++)
                data[i] = (byte)(((int)moves[i].Robot << 2) | (int)moves[i].Direction);
            return data;
        }

        /// <exception cref="FormatException">A byte doesn't describe a valid move.</exception>
        public static Move[] Decode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var moves = new Move[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] >= Robot.Count * 4)
                    throw new FormatException($"Byte {data[i]} at index {i} is not a move.");
                moves[i] = new Move((RobotColor)(data[i] >> 2), (Direction)(data[i] & 3));
            }
            return moves;
        }
    }
}
