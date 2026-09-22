namespace Faderio.Core.Domain;

/// <summary>
/// An ordered pair of tracks — the outgoing track and the incoming track — at a boundary in
/// a playback sequence. Directional: A→B and B→A are distinct adjacencies (ADR 0001).
/// </summary>
public sealed record Adjacency(Track Outgoing, Track Incoming);
