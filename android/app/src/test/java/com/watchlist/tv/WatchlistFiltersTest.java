package com.watchlist.tv;

import static org.junit.Assert.assertEquals;

import java.util.Arrays;
import java.util.List;
import java.util.Set;
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
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");
    }

    private static WatchlistItem item(
            String id,
            String mediaType,
            String availabilityStatus,
            List<String> services) {
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
                true,
                true,
                List.of("PL"),
                services,
                "2026-05-24T10:00:00+02:00",
                "2026-05-25T10:00:00+02:00");
    }

    @Test
    public void applyAvailabilityFilters_whenPlexAndPrimeSelected_returnsEitherMatch() {
        List<WatchlistItem> items = Arrays.asList(
                item("plex", "movie", "available_on_plex", List.of()),
                item("prime", "movie", "not_on_plex", List.of("Amazon Prime Video")),
                item("hbo", "movie", "not_on_plex", List.of("Max")),
                item("tv", "tv", "available_on_plex", List.of()));

        List<WatchlistItem> result = WatchlistFilters.applyAvailabilityFilters(
                items,
                BrowsingState.MEDIA_MOVIES,
                Set.of(BrowsingState.SERVICE_PLEX, BrowsingState.SERVICE_PRIME),
                false);

        assertEquals(2, result.size());
        assertEquals("plex", result.get(0).id());
        assertEquals("prime", result.get(1).id());
    }

    @Test
    public void applyAvailabilityFilters_whenHboSelected_matchesMaxAndHboNames() {
        List<WatchlistItem> items = Arrays.asList(
                item("max", "movie", "not_on_plex", List.of("Max")),
                item("hbo-max", "movie", "not_on_plex", List.of("HBO Max")),
                item("hbo", "movie", "not_on_plex", List.of("Hbo")),
                item("prime", "movie", "not_on_plex", List.of("Prime Video")));

        List<WatchlistItem> result = WatchlistFilters.applyAvailabilityFilters(
                items,
                BrowsingState.MEDIA_MOVIES,
                Set.of(BrowsingState.SERVICE_HBO),
                false);

        assertEquals(3, result.size());
        assertEquals("max", result.get(0).id());
        assertEquals("hbo-max", result.get(1).id());
        assertEquals("hbo", result.get(2).id());
    }

    @Test
    public void applyAvailabilityFilters_whenUnavailableSelected_returnsItemsWithoutSelectedServiceMatches() {
        List<WatchlistItem> items = Arrays.asList(
                item("plex", "movie", "available_on_plex", List.of()),
                item("prime", "movie", "not_on_plex", List.of("Prime Video")),
                item("unavailable", "movie", "not_on_plex", List.of()),
                item("unreleased", "movie", "unreleased", List.of()));

        List<WatchlistItem> result = WatchlistFilters.applyAvailabilityFilters(
                items,
                BrowsingState.MEDIA_MOVIES,
                Set.of(BrowsingState.SERVICE_PLEX),
                true);

        assertEquals(3, result.size());
        assertEquals("plex", result.get(0).id());
        assertEquals("unavailable", result.get(1).id());
        assertEquals("unreleased", result.get(2).id());
    }
}
