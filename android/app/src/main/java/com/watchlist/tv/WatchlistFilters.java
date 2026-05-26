package com.watchlist.tv;

import java.util.ArrayList;
import java.util.List;

public final class WatchlistFilters {
    public static final String MEDIA_MOVIE = "movie";
    public static final String MEDIA_TV = "tv";
    public static final String FILTER_ALL = "all";
    public static final String FILTER_AVAILABLE = "available";
    public static final String AVAILABLE_ON_PLEX = "available_on_plex";

    private WatchlistFilters() {
    }

    public static List<WatchlistItem> filterItems(
            List<WatchlistItem> items,
            String mediaType,
            String filter) {
        List<WatchlistItem> result = new ArrayList<>();

        for (WatchlistItem item : items) {
            if (!mediaType.equals(item.mediaType())) {
                continue;
            }

            if (FILTER_AVAILABLE.equals(filter)
                    && !AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
                continue;
            }

            result.add(item);
        }

        return result;
    }
}
