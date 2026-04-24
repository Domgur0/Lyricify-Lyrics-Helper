using Android.OS;
using Android.Runtime;
using Android.Util;
using Lyricify.Lyrics.App.ViewModels;
using Lyricify.Lyrics.Models;

namespace Lyricify.Lyrics.App.Platforms.Android;

/// <summary>
/// Publishes real-time lyrics to the SuperLyric Xposed module through the
/// "super_lyric" Android Binder service (<see href="https://github.com/HChenX/SuperLyricApi"/>).
/// <para>
/// The ISuperLyricManager AIDL wire protocol is implemented directly in C# using
/// JNI to reach the hidden <c>android.os.ServiceManager</c> API and
/// <see cref="Parcel"/> for serialization — no Java AAR dependency required.
/// </para>
/// </summary>
internal sealed class SuperLyricPublisher : IDisposable
{
    private const string Tag = "SuperLyricPublisher";
    private const string ServiceName = "super_lyric";

    // Interface descriptor written at the start of every transaction Parcel.
    private const string ManagerDescriptor = "com.hchen.superlyricapi.ISuperLyricManager";

    // Java class name stored in writeParcelable / readParcelable calls.
    private const string LineClassName = "com.hchen.superlyricapi.SuperLyricLine";

    // AIDL transaction codes (IBinder.FIRST_CALL_TRANSACTION = 1, ordered as declared in the .aidl).
    private const int TransactionRegisterPublisher = 1;
    private const int TransactionUnregisterPublisher = 2;
    private const int TransactionSendLyric = 4;
    private const int TransactionSendStop = 5;

    private IBinder? _binder;
    private bool _publisherRegistered;
    private int _lastLineIndex = -2; // -2 = never published

    private readonly LyricsViewModel _viewModel;

    internal SuperLyricPublisher(LyricsViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to bind to the SuperLyric service and register as a publisher.
    /// Does nothing if SuperLyric is not installed (service not found).
    /// </summary>
    public void Connect()
    {
        _binder = GetBinder();
        if (_binder is null)
        {
            Log.Info(Tag, "super_lyric service not found — SuperLyric is probably not installed.");
            return;
        }

        Transact(TransactionRegisterPublisher, null);
        _publisherRegistered = true;
        Log.Info(Tag, "Registered as SuperLyric publisher.");

        // Publish whatever lyric line is already active (e.g. service restarted mid-song).
        SendCurrentLineIfAny();
    }

    /// <summary>
    /// Unregisters from the service and stops publishing.  Call from <see cref="IDisposable.Dispose"/>.
    /// </summary>
    public void Disconnect()
    {
        if (_publisherRegistered && _binder is not null)
        {
            Transact(TransactionUnregisterPublisher, null);
            _publisherRegistered = false;
            Log.Info(Tag, "Unregistered from SuperLyric service.");
        }

        _binder = null;
    }

    public void Dispose() => Disconnect();

    // ── Public methods called by LyricsOverlayService ─────────────────────────

    /// <summary>
    /// Sends the lyric line at <paramref name="lineIndex"/> to SuperLyric.
    /// Skips the call when the same line was already sent last time.
    /// </summary>
    public void OnLineIndexChanged(int lineIndex)
    {
        if (!_publisherRegistered || _binder is null) return;
        if (lineIndex == _lastLineIndex) return;

        _lastLineIndex = lineIndex;

        if (lineIndex < 0 || lineIndex >= _viewModel.LyricLines.Count)
            return;

        SendLyric(_viewModel.LyricLines[lineIndex]);
    }

    /// <summary>
    /// Sends a stop event to SuperLyric (e.g. track ended or paused).
    /// </summary>
    public void OnPlaybackStopped()
    {
        if (!_publisherRegistered || _binder is null) return;

        _lastLineIndex = -2;
        Transact(TransactionSendStop, WriteStopData);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void SendCurrentLineIfAny()
    {
        var idx = _viewModel.CurrentLineIndex;
        if (idx >= 0 && idx < _viewModel.LyricLines.Count)
            SendLyric(_viewModel.LyricLines[idx]);
    }

    private void SendLyric(ILineInfo line)
    {
        Transact(TransactionSendLyric, data => WriteLyricData(data, line));
    }

    // ── Parcel writers ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a <c>SuperLyricData</c> Parcel payload with the given lyric line
    /// and current track metadata from the ViewModel.
    /// </summary>
    private void WriteLyricData(Parcel data, ILineInfo line)
    {
        // title, artist, album
        data.WriteString(_viewModel.TrackTitle);
        data.WriteString(_viewModel.ArtistName);
        data.WriteString(null); // album — not tracked

        // lyric (main line, with syllables when available)
        WriteParcelableLine(data, line, includeSyllables: true);

        // secondary — SubLine if present (e.g. romanisation embedded in the format)
        var secondary = line.SubLine;
        var pronunciation = (line as IFullLineInfo)?.Pronunciation;
        if (secondary is not null)
            WriteParcelableLine(data, secondary, includeSyllables: false);
        else if (!string.IsNullOrEmpty(pronunciation))
            WriteParcelableLineText(data, pronunciation!, line.StartTime ?? 0, line.EndTime ?? 0);
        else
            data.WriteString(null);

        // translation (Chinese or any tagged translation from IFullLineInfo)
        var translation = (line as IFullLineInfo)?.ChineseTranslation;
        if (!string.IsNullOrEmpty(translation))
            WriteParcelableLineText(data, translation!, line.StartTime ?? 0, line.EndTime ?? 0);
        else
            data.WriteString(null);

        WriteDeprecatedAndTrailingFields(data);
    }

    /// <summary>
    /// Writes a minimal <c>SuperLyricData</c> stop payload (title/artist preserved,
    /// no lyric line).
    /// </summary>
    private void WriteStopData(Parcel data)
    {
        data.WriteString(_viewModel.TrackTitle);
        data.WriteString(_viewModel.ArtistName);
        data.WriteString(null); // album
        data.WriteString(null); // lyric
        data.WriteString(null); // secondary
        data.WriteString(null); // translation
        WriteDeprecatedAndTrailingFields(data);
    }

    /// <summary>
    /// Writes the three deprecated / trailing fields that are always null:
    /// MediaMetadata, PlaybackState (both deprecated since API 3.3), base64Icon,
    /// and the extra Bundle.
    /// </summary>
    private static void WriteDeprecatedAndTrailingFields(Parcel data)
    {
        data.WriteString(null); // mediaMetadata  (writeParcelable null → writeString null)
        data.WriteString(null); // playbackState  (writeParcelable null → writeString null)
        data.WriteString(null); // base64Icon
        data.WriteInt(-1);      // extra Bundle null → writeInt(-1)
    }

    /// <summary>
    /// Writes a <c>SuperLyricLine</c> Parcelable into <paramref name="data"/>,
    /// mimicking Android's <c>Parcel.writeParcelable()</c> format:
    /// <list type="number">
    ///   <item>class-name string (used by readParcelable to find the CREATOR)</item>
    ///   <item>SuperLyricLine.writeToParcel data</item>
    /// </list>
    /// Writes a null string when <paramref name="line"/> is null.
    /// </summary>
    private static void WriteParcelableLine(Parcel data, ILineInfo? line, bool includeSyllables)
    {
        if (line is null)
        {
            data.WriteString(null);
            return;
        }

        data.WriteString(LineClassName); // writeParcelableCreator

        // SuperLyricLine.writeToParcel:
        //   writeString(text)
        //   writeTypedArray(words, flags)
        //   writeLong(startTime)
        //   writeLong(endTime)
        //   writeLong(delay)   — deprecated, always 0
        data.WriteString(line.Text);

        if (includeSyllables && line is SyllableLineInfo syllableLine
            && syllableLine.Syllables is { Count: > 0 })
        {
            WriteTypedSyllableArray(data, syllableLine.Syllables);
        }
        else
        {
            data.WriteInt(-1); // writeTypedArray null → writeInt(-1)
        }

        data.WriteLong(line.StartTime ?? 0L);
        data.WriteLong(line.EndTime ?? 0L);
        data.WriteLong(0L); // delay (deprecated)
    }

    /// <summary>
    /// Writes a text-only <c>SuperLyricLine</c> Parcelable (no syllables).
    /// </summary>
    private static void WriteParcelableLineText(Parcel data, string text, long startTime, long endTime)
    {
        data.WriteString(LineClassName);
        data.WriteString(text);
        data.WriteInt(-1); // no words
        data.WriteLong(startTime);
        data.WriteLong(endTime);
        data.WriteLong(0L); // delay (deprecated)
    }

    /// <summary>
    /// Writes a <c>SuperLyricWord[]</c> typed array using Android's
    /// <c>Parcel.writeTypedArray</c> wire format:
    /// <c>length</c> + for each element: <c>1</c> (non-null marker)
    /// + <c>SuperLyricWord.writeToParcel</c> fields.
    /// </summary>
    private static void WriteTypedSyllableArray(Parcel data, List<ISyllableInfo> syllables)
    {
        data.WriteInt(syllables.Count);
        foreach (var s in syllables)
        {
            data.WriteInt(1); // non-null marker
            // SuperLyricWord.writeToParcel: word, delay (deprecated), startTime, endTime
            data.WriteString(s.Text);
            data.WriteLong(0L); // delay (deprecated)
            data.WriteLong(s.StartTime);
            data.WriteLong(s.EndTime);
        }
    }

    // ── Binder transport ──────────────────────────────────────────────────────

    private void Transact(int code, Action<Parcel>? writeData)
    {
        if (_binder is null) return;

        Parcel? parcelData = null;
        Parcel? parcelReply = null;
        try
        {
            parcelData = Parcel.Obtain();
            parcelReply = Parcel.Obtain();
            if (parcelData is null || parcelReply is null)
            {
                Log.Warn(Tag, $"Transact({code}): Parcel.Obtain() returned null.");
                return;
            }

            parcelData.WriteInterfaceToken(ManagerDescriptor);
            writeData?.Invoke(parcelData);
            _binder.Transact(code, parcelData, parcelReply, 0);
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"Transact({code}) threw: {ex.GetType().Name} — {ex.Message}");
            // If the remote died, clear the binder so subsequent calls bail early.
            if (ex is Java.Lang.Exception)
                _binder = null;
        }
        finally
        {
            parcelReply?.Recycle();
            parcelData?.Recycle();
        }
    }

    // ── Hidden ServiceManager access via JNI ──────────────────────────────────

    /// <summary>
    /// Calls the hidden <c>android.os.ServiceManager.getService("super_lyric")</c>
    /// via JNI and returns the resulting <see cref="IBinder"/>, or <c>null</c>
    /// when the service is unavailable.
    /// </summary>
    private static IBinder? GetBinder()
    {
        try
        {
            var cls = JNIEnv.FindClass("android/os/ServiceManager");
            if (cls == IntPtr.Zero) return null;

            try
            {
                var mid = JNIEnv.GetStaticMethodID(cls, "getService",
                    "(Ljava/lang/String;)Landroid/os/IBinder;");
                if (mid == IntPtr.Zero) return null;

                var nameHandle = JNIEnv.NewString(ServiceName);
                try
                {
                    var handle = JNIEnv.CallStaticObjectMethod(cls, mid, new JValue(nameHandle));
                    if (handle == IntPtr.Zero) return null;
                    return Java.Lang.Object.GetObject<IBinder>(
                        handle, JniHandleOwnership.TransferLocalRef);
                }
                finally
                {
                    JNIEnv.DeleteLocalRef(nameHandle);
                }
            }
            finally
            {
                JNIEnv.DeleteLocalRef(cls);
            }
        }
        catch (Exception ex)
        {
            Log.Warn(Tag, $"GetBinder failed: {ex.GetType().Name} — {ex.Message}");
            return null;
        }
    }
}
