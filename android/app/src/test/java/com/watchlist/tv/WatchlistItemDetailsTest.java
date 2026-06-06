package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.List;
import org.junit.Test;

public class WatchlistItemDetailsTest {
    @Test
    public void metadataSummary_hidesMissingValuesAndRequiresMeaningfulVoteCount() {
        WatchlistItemDetails details = createDetails(
                List.of("Drama", "Comedy"),
                93,
                "en",
                7.7,
                9);

        assertEquals("1977 • 1h 33m • EN • Drama, Comedy", details.metadataSummary());
    }

    @Test
    public void metadataSummary_includesTmdbScoreWhenVoteCountIsAtLeastTen() {
        WatchlistItemDetails details = createDetails(
                List.of("Comedy"),
                93,
                "en",
                7.7,
                10);

        assertEquals("1977 • 1h 33m • EN • Comedy • 7.7 TMDB", details.metadataSummary());
    }

    private static WatchlistItemDetails createDetails(
            List<String> genres,
            Integer runtimeMinutes,
            String language,
            Double score,
            Integer voteCount) {
        return new WatchlistItemDetails(
                "movie-annie-hall", "movie", "letterboxd", "source",
                "Annie Hall", 1977, "Overview", null, null,
                "released", "available_on_plex",
                false, false, List.of(), List.of(),
                "2026-05-24T10:00:00+02:00", "2026-05-25T10:00:00+02:00",
                genres, runtimeMinutes, language, score, voteCount,
                "Open in Plex", true, null);
    }
}
