package com.watchlist.tv;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

public final class WatchlistItemDetails {
    private final String id;
    private final String mediaType;
    private final String source;
    private final String sourceId;
    private final String title;
    private final Integer year;
    private final String overview;
    private final String posterUrl;
    private final String backdropUrl;
    private final String releaseStatus;
    private final String availabilityStatus;
    private final String libraryMembership;
    private final boolean vodReleaseKnown;
    private final boolean releasedOnVod;
    private final List<String> vodRegions;
    private final List<String> ownedServiceAvailability;
    private final String addedAt;
    private final String updatedAt;
    private final List<String> genres;
    private final Integer runtimeMinutes;
    private final String originalLanguage;
    private final Double tmdbVoteAverage;
    private final Integer tmdbVoteCount;
    private final String primaryActionLabel;
    private final boolean primaryActionEnabled;
    private final String primaryActionTarget;

    public WatchlistItemDetails(
            String id, String mediaType, String source, String sourceId,
            String title, Integer year, String overview,
            String posterUrl, String backdropUrl,
            String releaseStatus, String availabilityStatus,
            boolean vodReleaseKnown, boolean releasedOnVod,
            List<String> vodRegions, List<String> ownedServiceAvailability,
            String addedAt, String updatedAt,
            List<String> genres, Integer runtimeMinutes,
            String originalLanguage, Double tmdbVoteAverage, Integer tmdbVoteCount,
            String primaryActionLabel, boolean primaryActionEnabled,
            String primaryActionTarget) {
        this(
                id, mediaType, source, sourceId,
                title, year, overview,
                posterUrl, backdropUrl,
                releaseStatus, availabilityStatus,
                WatchlistItem.MEMBERSHIP_WATCHLIST,
                vodReleaseKnown, releasedOnVod,
                vodRegions, ownedServiceAvailability,
                addedAt, updatedAt,
                genres, runtimeMinutes,
                originalLanguage, tmdbVoteAverage, tmdbVoteCount,
                primaryActionLabel, primaryActionEnabled,
                primaryActionTarget);
    }

    public WatchlistItemDetails(
            String id, String mediaType, String source, String sourceId,
            String title, Integer year, String overview,
            String posterUrl, String backdropUrl,
            String releaseStatus, String availabilityStatus,
            String libraryMembership,
            boolean vodReleaseKnown, boolean releasedOnVod,
            List<String> vodRegions, List<String> ownedServiceAvailability,
            String addedAt, String updatedAt,
            List<String> genres, Integer runtimeMinutes,
            String originalLanguage, Double tmdbVoteAverage, Integer tmdbVoteCount,
            String primaryActionLabel, boolean primaryActionEnabled,
            String primaryActionTarget) {
        this.id = id;
        this.mediaType = mediaType;
        this.source = source;
        this.sourceId = sourceId;
        this.title = title;
        this.year = year;
        this.overview = overview;
        this.posterUrl = posterUrl;
        this.backdropUrl = backdropUrl;
        this.releaseStatus = releaseStatus;
        this.availabilityStatus = availabilityStatus;
        this.libraryMembership = libraryMembership;
        this.vodReleaseKnown = vodReleaseKnown;
        this.releasedOnVod = releasedOnVod;
        this.vodRegions = Collections.unmodifiableList(new ArrayList<>(vodRegions));
        this.ownedServiceAvailability = Collections.unmodifiableList(new ArrayList<>(ownedServiceAvailability));
        this.addedAt = addedAt;
        this.updatedAt = updatedAt;
        this.genres = Collections.unmodifiableList(new ArrayList<>(genres));
        this.runtimeMinutes = runtimeMinutes;
        this.originalLanguage = originalLanguage;
        this.tmdbVoteAverage = tmdbVoteAverage;
        this.tmdbVoteCount = tmdbVoteCount;
        this.primaryActionLabel = primaryActionLabel;
        this.primaryActionEnabled = primaryActionEnabled;
        this.primaryActionTarget = primaryActionTarget;
    }

    public static WatchlistItemDetails fromItem(WatchlistItem item) {
        return new WatchlistItemDetails(
                item.id(), item.mediaType(), item.source(), item.sourceId(),
                item.title(), item.year(), item.overview(),
                item.posterUrl(), item.backdropUrl(),
                item.releaseStatus(), item.availabilityStatus(),
                item.libraryMembership(),
                item.vodReleaseKnown(), item.releasedOnVod(),
                item.vodRegions(), item.ownedServiceAvailability(),
                item.addedAt(), item.updatedAt(),
                List.of(), null, null, null, null,
                MainActivity.formatAvailability(item),
                WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus()),
                null);
    }

    public String metadataSummary() {
        List<String> parts = new ArrayList<>();
        if (year != null) {
            parts.add(String.valueOf(year));
        }
        if (runtimeMinutes != null && runtimeMinutes > 0) {
            parts.add(formatRuntime(runtimeMinutes));
        }
        if (originalLanguage != null && !originalLanguage.isEmpty()) {
            parts.add(originalLanguage.toUpperCase(Locale.ROOT));
        }
        if (!genres.isEmpty()) {
            parts.add(String.join(", ", genres));
        }
        if (tmdbVoteAverage != null && tmdbVoteCount != null && tmdbVoteCount >= 10) {
            parts.add(String.format(Locale.US, "%.1f TMDB", tmdbVoteAverage));
        }
        return String.join(" \u2022 ", parts);
    }

    public boolean isPlexOnly() {
        return WatchlistItem.MEMBERSHIP_PLEX_ONLY.equals(libraryMembership);
    }

    private static String formatRuntime(int minutes) {
        int hours = minutes / 60;
        int remainingMinutes = minutes % 60;
        if (hours <= 0) {
            return remainingMinutes + "m";
        }
        if (remainingMinutes == 0) {
            return hours + "h";
        }
        return hours + "h " + remainingMinutes + "m";
    }

    public String id() { return id; }
    public String mediaType() { return mediaType; }
    public String source() { return source; }
    public String sourceId() { return sourceId; }
    public String title() { return title; }
    public Integer year() { return year; }
    public String overview() { return overview; }
    public String posterUrl() { return posterUrl; }
    public String backdropUrl() { return backdropUrl; }
    public String releaseStatus() { return releaseStatus; }
    public String availabilityStatus() { return availabilityStatus; }
    public String libraryMembership() { return libraryMembership; }
    public boolean vodReleaseKnown() { return vodReleaseKnown; }
    public boolean releasedOnVod() { return releasedOnVod; }
    public List<String> vodRegions() { return vodRegions; }
    public List<String> ownedServiceAvailability() { return ownedServiceAvailability; }
    public String addedAt() { return addedAt; }
    public String updatedAt() { return updatedAt; }
    public List<String> genres() { return genres; }
    public Integer runtimeMinutes() { return runtimeMinutes; }
    public String originalLanguage() { return originalLanguage; }
    public Double tmdbVoteAverage() { return tmdbVoteAverage; }
    public Integer tmdbVoteCount() { return tmdbVoteCount; }
    public String primaryActionLabel() { return primaryActionLabel; }
    public boolean primaryActionEnabled() { return primaryActionEnabled; }
    public String primaryActionTarget() { return primaryActionTarget; }
}
