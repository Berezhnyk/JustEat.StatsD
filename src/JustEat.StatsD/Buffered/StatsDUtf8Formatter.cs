using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace JustEat.StatsD.Buffered
{
    internal sealed class StatsDUtf8Formatter
    {
        private readonly byte[] _utf8Prefix;

        public StatsDUtf8Formatter(string prefix)
        {
            _utf8Prefix = string.IsNullOrWhiteSpace(prefix) ?
                Array.Empty<byte>() :
                Encoding.UTF8.GetBytes(prefix + ".");
        }

        public int GetMaxBufferSize(in StatsDMessage msg)
        {
            const int MaxSerializedDoubleSymbols = 32;
            const int ColonBytes = 1;

            const int MaxMessageKindSuffixSize = 3;
            const int MaxSamplingSuffixSize = 2;

            return _utf8Prefix.Length
                    + Encoding.UTF8.GetByteCount(msg.StatBucket)
                    + ColonBytes
                    + MaxSerializedDoubleSymbols
                    + MaxMessageKindSuffixSize
                    + MaxSamplingSuffixSize
                    + MaxSerializedDoubleSymbols;
        }

        public bool TryFormat(in StatsDMessage msg, double sampleRate, IEnumerable<KeyValuePair<string, string>> tags, Span<byte> destination, out int written)
        {
            var buffer = new Buffer(destination);

            bool isFormattingSuccessful =
                  TryWriteBucketNameWithColon(ref buffer, msg.StatBucket)
               && TryWritePayloadWithMessageKindSuffix(ref buffer, msg)
               && TryWriteSampleRateIfNeeded(ref buffer, sampleRate)
               && TryWriteTagsIfNeeded(ref buffer, tags);

            written = isFormattingSuccessful ? buffer.Written : 0;
            return isFormattingSuccessful;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryWriteBucketNameWithColon(ref Buffer buffer, string statBucket)
        {
            // prefix + msg.Bucket + ":"

            return buffer.TryWriteBytes(_utf8Prefix)
                && buffer.TryWriteUtf8String(statBucket)
                && buffer.TryWriteByte((byte)':');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryWritePayloadWithMessageKindSuffix(ref Buffer buffer, in StatsDMessage msg)
        {
            // msg.Value + (<oneOf> "|ms", "|c", "|g")

            var integralMagnitude = (long)msg.Magnitude;

            switch (msg.MessageKind)
            {
                case StatsDMessageKind.Counter:
                    {
                        return buffer.TryWriteInt64(integralMagnitude)
                            && buffer.TryWriteBytes((byte)'|', (byte)'c');
                    }
                case StatsDMessageKind.Timing:
                    {
                        return buffer.TryWriteInt64(integralMagnitude)
                            && buffer.TryWriteBytes((byte)'|', (byte)'m', (byte)'s');
                    }
                case StatsDMessageKind.Gauge:
                    {
                        // check if magnitude is integral, integers are written significantly faster
                        bool isMagnitudeIntegral = msg.Magnitude == integralMagnitude;

                        var successSoFar = isMagnitudeIntegral ?
                            buffer.TryWriteInt64(integralMagnitude) :
                            buffer.TryWriteDouble(msg.Magnitude);

                        return successSoFar && buffer.TryWriteBytes((byte)'|', (byte)'g');
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(msg));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryWriteSampleRateIfNeeded(ref Buffer buffer, double sampleRate)
        {
            // {<optional> "|@" + sampleRate}

            if (sampleRate < 1.0 && sampleRate > 0.0)
            {
                return buffer.TryWriteBytes((byte)'|', (byte)'@')
                    && buffer.TryWriteDouble(sampleRate);
            }

            return true;
        }

        private static bool TryWriteTagsIfNeeded(ref Buffer buffer, IEnumerable<KeyValuePair<string, string>> tags)
        {
            // {<optional> "#tag1:value1,tag2=value2"
            var tagsArray = tags as KeyValuePair<string, string>[] ?? tags.ToArray();
            if (!tagsArray.Any())
            {
                return true;
            }

            var sb = new StringBuilder("#");
            var lastIndex = tagsArray.Length - 1;
            for (var i = 0; i < tagsArray.Length; i++)
            {
                var tag = tagsArray[i];
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1}", tag.Key, tag.Value);
                if (i != lastIndex)
                {
                    sb.Append(",");
                }
            }

            return buffer.TryWriteUtf8String(sb.ToString());
        }
    }
}
