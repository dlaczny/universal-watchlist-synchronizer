package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.List;
import org.junit.Test;

public class WatchlistApiClientTest {
    @Test
    public void parseItems_parsesBackendWatchlistJson() throws Exception {
        String json = "[{"
                + "\"id\":\"movie-dune-part-two\","
                + "\"mediaType\":\"movie\","
                + "\"source\":\"letterboxd\","
                + "\"sourceId\":\"letterboxd-dune-part-two\","
                + "\"title\":\"Dune: Part Two\","
                + "\"year\":2024,"
                + "\"overview\":\"Overview\","
                + "\"posterUrl\":\"https://image.example/poster.jpg\","
                + "\"backdropUrl\":\"https://image.example/backdrop.jpg\","
                + "\"releaseStatus\":\"released\","
                + "\"availabilityStatus\":\"available_on_plex\","
                + "\"updatedAt\":\"2026-05-25T10:00:00+02:00\""
                + "}]";

        List<WatchlistItem> items = WatchlistApiClient.parseItems(json);

        assertEquals(1, items.size());
        WatchlistItem item = items.get(0);
        assertEquals("movie-dune-part-two", item.id());
        assertEquals("movie", item.mediaType());
        assertEquals("Dune: Part Two", item.title());
        assertEquals("available_on_plex", item.availabilityStatus());
    }

    @Test
    public void parseSyncStatus_parsesSeededStatus() throws Exception {
        String json = "{"
                + "\"status\":\"seeded\","
                + "\"lastSuccessfulSyncAt\":\"2026-05-25T10:00:00+02:00\""
                + "}";

        SyncStatus status = WatchlistApiClient.parseSyncStatus(json);

        assertEquals("seeded", status.status());
        assertEquals("2026-05-25T10:00:00+02:00", status.lastSuccessfulSyncAt());
    }
}
