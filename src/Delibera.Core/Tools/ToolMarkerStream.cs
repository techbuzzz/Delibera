using System.Text;
using System.Text.RegularExpressions;

namespace Delibera.Core.Tools;

/// <summary>
///    Incremental splitter for <c>[[TOOL: name {json}]]</c> markers.
/// </summary>
/// <remarks>
///    <para>
///    A streaming response delivers text in arbitrary chunks, so a marker can straddle a chunk
///    boundary. Buffering the tail from the last <c>[[</c> until the marker either completes or
///    the stream ends is what makes detection correct rather than merely usually-right: emitting
///    on the first incomplete match would truncate the arguments and, worse, emit a call the model
///    never finished writing.
///    </para>
///    <para>
///    The buffer is bounded. A model that emits an unterminated <c>[[</c> and then megabytes of
///    prose would otherwise accumulate the whole response in memory — the tool path is exactly
///    where an adversarial or confused model is most likely to produce one.
///    </para>
/// </remarks>
public sealed class ToolMarkerStream
{
   /// <summary>Marker opener the buffer waits on.</summary>
   private const string MarkerStart = "[[";

   /// <summary>
   ///   Largest tail held while waiting for a marker to complete. Beyond this the text is emitted
   ///   as-is, on the grounds that no plausible tool marker is that long.
   /// </summary>
   private const int MaxCarryLength = 8_192;

   private readonly StringBuilder _pending = new();

   /// <summary>Text that could be the start of a marker, held back until it resolves.</summary>
   /// <param name="chunk">Newly received text, appended to whatever was held.</param>
   /// <param name="marker">The completed marker, when one finished in this chunk.</param>
   /// <returns>Text that is safe to emit as plain text right now.</returns>
   public string Append(string chunk, out ToolCallParser.Request? marker)
   {
      marker = null;

      if (!string.IsNullOrEmpty(chunk))
         _pending.Append(chunk);

      var emit = new StringBuilder();

      while (true)
      {
         var buffer = _pending.ToString();
         var start = buffer.IndexOf(MarkerStart, StringComparison.Ordinal);

         if (start < 0)
         {
            // No opener anywhere: everything buffered is plain text.
            emit.Append(buffer);
            _pending.Clear();
            break;
         }

         if (start > 0)
         {
            emit.Append(buffer, 0, start);
         }

         var candidate = buffer[start..];

         // A complete match ends with "]]" anywhere after the opener.
         var end = candidate.IndexOf("]]", StringComparison.Ordinal);
         if (end < 0)
         {
            // Incomplete. Hold from the opener, unless the hold has grown past the bound — then
            // give up on it and treat it as prose, so one stuck marker cannot stall the stream.
            if (candidate.Length > MaxCarryLength)
            {
               emit.Append(candidate);
               _pending.Clear();
            }
            else
            {
               _pending.Clear();
               _pending.Append(candidate);
            }

            break;
         }

         var markerText = candidate[..(end + 2)];
         var parsed = ToolCallParser.TryParseSingle(markerText);
         if (parsed is null)
         {
            // "[[" that is not a tool marker at all (e.g. a literal example in prose). Emit the
            // opener as text and keep scanning the rest.
            emit.Append(MarkerStart);
            _pending.Clear();
            _pending.Append(candidate[MarkerStart.Length..]);
            continue;
         }

         marker = parsed;
         _pending.Clear();
         _pending.Append(candidate[(end + 2)..]);

         // Only one marker is reported per Append; the rest stay buffered for the next call.
         break;
      }

      return emit.ToString();
   }

   /// <summary>
   ///   Emits whatever is still buffered once the stream ends, so a response that ends mid-marker
   ///   still delivers its text rather than silently swallowing the tail.
   /// </summary>
   public string Flush()
   {
      var remainder = _pending.ToString();
      _pending.Clear();
      return remainder;
   }
}