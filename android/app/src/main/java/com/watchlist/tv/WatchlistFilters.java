package com.watchlist.tv;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;

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

    public static List<WatchlistItem> applyAvailabilityFilters(
            List<WatchlistItem> items,
            String mediaType,
            Set<String> selectedServices,
            boolean includeUnavailable) {
        List<WatchlistItem> result = new ArrayList<>();

        for (WatchlistItem item : items) {
            if (!BrowsingState.MEDIA_ALL.equals(mediaType) && !mediaType.equals(item.mediaType())) {
                continue;
            }

            boolean selectedServiceMatch = matchesAnySelectedService(item, selectedServices);
            boolean unavailableMatch = includeUnavailable && !matchesAnyKnownAvailabilityService(item);
            if (selectedServiceMatch || unavailableMatch) {
                result.add(item);
            }
        }

        return result;
    }

    private static boolean matchesAnySelectedService(WatchlistItem item, Set<String> selectedServices) {
        for (String service : selectedServices) {
            if (matchesService(item, service)) {
                return true;
            }
        }
        return false;
    }

    private static boolean matchesService(WatchlistItem item, String service) {
        if (BrowsingState.SERVICE_PLEX.equals(service)) {
            return AVAILABLE_ON_PLEX.equals(item.availabilityStatus());
        }

        for (String provider : item.ownedServiceAvailability()) {
            if (service.equals(normalizeProvider(provider))) {
                return true;
            }
        }

        return false;
    }

    private static boolean matchesAnyKnownAvailabilityService(WatchlistItem item) {
        return matchesService(item, BrowsingState.SERVICE_PLEX)
                || matchesService(item, BrowsingState.SERVICE_PRIME)
                || matchesService(item, BrowsingState.SERVICE_HBO)
                || matchesService(item, BrowsingState.SERVICE_SKYSHOWTIME)
                || matchesService(item, BrowsingState.SERVICE_CRUNCHYROLL);
    }

    private static String normalizeProvider(String providerName) {
        String provider = providerName.trim().toLowerCase(Locale.US);
        if ("amazon prime video".equals(provider) || "prime video".equals(provider) || "prime".equals(provider)) {
            return BrowsingState.SERVICE_PRIME;
        }
        if ("max".equals(provider) || "hbo max".equals(provider) || "hbo".equals(provider)) {
            return BrowsingState.SERVICE_HBO;
        }
        if ("skyshowtime".equals(provider)) {
            return BrowsingState.SERVICE_SKYSHOWTIME;
        }
        if ("crunchyroll".equals(provider)) {
            return BrowsingState.SERVICE_CRUNCHYROLL;
        }
        return provider;
    }
}
