using System.Text.Json.Serialization;
using LionGrep.Models;

namespace LionGrep.Services;

/// <summary>
/// Source-generated JSON metadata for every type the app serializes to the registry. Lets
/// JsonSerializer reach those types without reflection, which is required for trim-safe
/// (Release/MSIX) builds — the non-generic reflection-based overloads emit IL2026 warnings.
/// Add a <see cref="JsonSerializableAttribute"/> entry whenever a new type starts being persisted.
/// </summary>
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(List<Preset>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class JsonContext : JsonSerializerContext;
