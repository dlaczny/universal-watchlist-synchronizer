package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.Arrays;
import java.util.List;
import org.junit.Test;

public class WatchlistFiltersTest {
    @Test
    public void filterItems_whenMovieAll_returnsOnlyMovies() {
        List<WatchlistItem> items = Arrays.asList(
                item("movie-1", "movie", "not_on_plex"),
                item("tv-1", "tv", "available_on_plex"));

        List<WatchlistItem> result = WatchlistFilters.filterItems(items, "movie", "all");

        assertEquals(1, result.size());
        assertEquals("movie-1", result.get(0).id());
    }

    @Test
    public void filterItems_whenAvailable_returnsOnlyPlexAvailableItems() {
        List<WatchlistItem> items = Arrays.asList(
                item("movie-1", "movie", "available_on_plex"),
                item("movie-2", "movie", "not_on_plex"),
                item("movie-3", "movie", "unreleased"));

        List<WatchlistItem> result = WatchlistFilters.filterItems(items, "movie", "available");

        assertEquals(1, result.size());
        assertEquals("movie-1", result.get(0).id());
    }

    private static WatchlistItem item(String id, String mediaType, String availabilityStatus) {
        return new WatchlistItem(
                id,
                mediaType,
                "letterboxd",
                id + "-source",
                "Title " + id,
                2024,
                "Overview",
                null,
                null,
                "released",
                availabilityStatus,
                "2026-05-25T10:00:00+02:00");
    }
}
