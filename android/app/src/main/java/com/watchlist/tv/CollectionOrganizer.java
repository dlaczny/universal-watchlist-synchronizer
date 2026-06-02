package com.watchlist.tv;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

public final class CollectionOrganizer {
    public static final String SORT_DATE_ADDED = "date_added";
    public static final String SORT_ALPHABETICAL = "alphabetical";

    private CollectionOrganizer() {
    }

    public static List<WatchlistItem> organize(
            List<WatchlistItem> items, boolean includeUnavailable, String sortMode) {
        List<WatchlistItem> organizedItems = new ArrayList<>();
        for (WatchlistItem item : items) {
            if (includeUnavailable || WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
                organizedItems.add(item);
            }
        }

        if (SORT_ALPHABETICAL.equals(sortMode)) {
            Collections.sort(organizedItems, new Comparator<WatchlistItem>() {
                @Override
                public int compare(WatchlistItem first, WatchlistItem second) {
                    return String.CASE_INSENSITIVE_ORDER.compare(first.title(), second.title());
                }
            });
        }

        return organizedItems;
    }
}
