using System;
using System.Security.Cryptography;
using System.Text;

namespace ChatVoice
{
	/// <summary>
	/// Everything about a player's voice comes from sha256(username.casefold()):
	///
	///   bytes 0-3  speaker id within the model
	///   bytes 4-5  speaking rate       (length_scale)
	///   bytes 6-7  speaking variation  (noise_w_scale)
	///
	/// It has to be a stable hash. String.GetHashCode() is randomized per
	/// process in .NET Core and would give a player a different voice on every
	/// game launch.
	///
	/// The rate and variation jitter exist because Spanish only has a 2-speaker
	/// model available. English (~900 speakers) would not need it.
	/// </summary>
	internal readonly struct VoiceParams
	{
		public readonly int SpeakerId;
		public readonly float LengthScale;
		public readonly float NoiseWScale;

		public VoiceParams(int speakerId, float lengthScale, float noiseWScale)
		{
			SpeakerId = speakerId;
			LengthScale = lengthScale;
			NoiseWScale = noiseWScale;
		}

		public override string ToString() =>
			$"spk={SpeakerId} rate={LengthScale:F3} var={NoiseWScale:F3}";
	}

	internal static class VoiceAssigner
	{
		private const float LengthScaleMin = 0.92f;
		private const float LengthScaleMax = 1.14f;
		private const float NoiseWMin = 0.60f;
		private const float NoiseWMax = 1.00f;

		public static VoiceParams For(string username, int numSpeakers)
		{
			byte[] digest = SHA256.HashData(
				Encoding.UTF8.GetBytes((username ?? string.Empty).Trim().ToLowerInvariant()));

			uint speakerBits = ReadUInt32(digest, 0);
			uint rateBits = ReadUInt16(digest, 4);
			uint varBits = ReadUInt16(digest, 6);

			int speakers = Math.Max(numSpeakers, 1);
			int speakerId = (int)(speakerBits % (uint)speakers);

			float length = Lerp(LengthScaleMin, LengthScaleMax, rateBits / 65535f);
			float noiseW = Lerp(NoiseWMin, NoiseWMax, varBits / 65535f);

			return new VoiceParams(speakerId, length, noiseW);
		}

		// Big-endian, to match the reference implementation this was ported from.
		private static uint ReadUInt32(byte[] b, int offset) =>
			((uint)b[offset] << 24) | ((uint)b[offset + 1] << 16) |
			((uint)b[offset + 2] << 8) | b[offset + 3];

		private static uint ReadUInt16(byte[] b, int offset) =>
			((uint)b[offset] << 8) | b[offset + 1];

		private static float Lerp(float lo, float hi, float t) => lo + (hi - lo) * t;
	}
}
