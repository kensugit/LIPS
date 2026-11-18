namespace CatalogSearch.Infrastructure;
public static class CandidatePolicy
{
    public static bool Matches(string name, int? volume, string vintage, string producerJa, string producerEn,
        string candidateName, int candidateVolume, string candidateVintage, string candidateProducerJa, string candidateProducerEn)
    {
        if (volume is null or <= 0 || string.IsNullOrWhiteSpace(vintage) || ReviewService.Normalize(name).Length == 0) return false;
        if (volume != candidateVolume || vintage != candidateVintage || ReviewService.Normalize(name) != ReviewService.Normalize(candidateName)) return false;
        // Compare only languages present on both sides; don't infer aliases or translations.
        var pairs = new[] { (producerJa, candidateProducerJa), (producerEn, candidateProducerEn) }
            .Where(x => !string.IsNullOrWhiteSpace(x.Item1) && !string.IsNullOrWhiteSpace(x.Item2)).ToArray();
        return pairs.Length == 0 || pairs.All(x => ReviewService.Normalize(x.Item1) == ReviewService.Normalize(x.Item2));
    }
}
