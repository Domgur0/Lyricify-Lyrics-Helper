namespace Lyricify.Lyrics.Models
{
    /// <summary>
    /// 歌词同步结果，包含当前播放位置所对应的行、音节及进度信息
    /// </summary>
    public class SyncResult
    {
        /// <summary>
        /// 当前行的索引（基于 0）。
        /// 如果播放位置在第一行之前（或歌词列表为空），则为 -1。
        /// </summary>
        public int LineIndex { get; set; } = -1;

        /// <summary>
        /// 当前音节在当前行中的索引（基于 0）。
        /// 如果当前行不是逐字同步行，或播放位置在第一个音节之前，则为 -1。
        /// </summary>
        public int SyllableIndex { get; set; } = -1;

        /// <summary>
        /// 播放位置在当前行持续时间内的进度，范围为 [0.0, 1.0]。
        /// 0.0 表示行开始，1.0 表示行结束。
        /// 如果当前行没有结束时间，则为 0.0。
        /// </summary>
        public double LineProgress { get; set; } = 0.0;

        /// <summary>
        /// 播放位置在当前音节持续时间内的进度，范围为 [0.0, 1.0]。
        /// 0.0 表示音节开始，1.0 表示音节结束。
        /// 如果 <see cref="SyllableIndex"/> 为 -1，则为 0.0。
        /// </summary>
        public double SyllableProgress { get; set; } = 0.0;

        /// <summary>
        /// 当前行的引用。如果 <see cref="LineIndex"/> 为 -1，则为 null。
        /// </summary>
        public ILineInfo? CurrentLine { get; set; }

        /// <summary>
        /// 当前音节的引用。如果 <see cref="SyllableIndex"/> 为 -1，则为 null。
        /// </summary>
        public ISyllableInfo? CurrentSyllable { get; set; }
    }
}
