package com.watchlist.tv;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

public final class WatchlistApiClient {
    private final String baseUrl;

    public WatchlistApiClient(String baseUrl) {
        this.baseUrl = trimTrailingSlash(baseUrl);
    }

    public List<WatchlistItem> getWatchlist(
            String collection,
            String sortMode,
            boolean includeUnavailable) throws IOException, JSONException {
        String json = get(buildWatchlistPath(collection, sortMode, includeUnavailable));
        return parseItems(json);
    }

    public static String buildWatchlistPath(String mediaType, String sortMode, boolean includeUnavailable) {
        String collection = BrowsingState.MEDIA_TV.equals(mediaType)
                ? "tv"
                : BrowsingState.MEDIA_MOVIES.equals(mediaType) ? "movie" : "all";
        String sort = CollectionOrganizer.SORT_ALPHABETICAL.equals(sortMode)
                ? "title_asc"
                : "added_desc";
        String availability = includeUnavailable
                ? "plex,not_on_plex,unreleased,unknown_match"
                : "plex";
        return "/api/watchlist?collection=" + collection
                + "&availability=" + availability
                + "&sort=" + sort;
    }

    public SyncStatus getSyncStatus() throws IOException, JSONException {
        return parseSyncStatus(get("/api/sync/status"));
    }

    public static List<WatchlistItem> parseItems(String json) throws JSONException {
        JSONArray array = new JSONArray(json);
        List<WatchlistItem> items = new ArrayList<>();

        for (int index = 0; index < array.length(); index++) {
            JSONObject item = array.getJSONObject(index);
            items.add(new WatchlistItem(
                    item.getString("id"),
                    item.getString("mediaType"),
                    item.getString("source"),
                    item.getString("sourceId"),
                    item.getString("title"),
                    item.has("year") && !item.isNull("year") ? item.getInt("year") : null,
                    nullableString(item, "overview"),
                    nullableString(item, "posterUrl"),
                    nullableString(item, "backdropUrl"),
                    item.getString("releaseStatus"),
                    item.getString("availabilityStatus"),
                    item.getString("addedAt"),
                    item.getString("updatedAt")));
        }

        return items;
    }

    public static SyncStatus parseSyncStatus(String json) throws JSONException {
        JSONObject object = new JSONObject(json);
        return new SyncStatus(
                object.getString("status"),
                object.getString("lastSuccessfulSyncAt"));
    }

    private static String nullableString(JSONObject object, String name) throws JSONException {
        return object.has(name) && !object.isNull(name) ? object.getString(name) : null;
    }

    private String get(String path) throws IOException {
        URL url = new URL(baseUrl + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setConnectTimeout(5000);
        connection.setReadTimeout(5000);
        connection.setRequestMethod("GET");

        int statusCode = connection.getResponseCode();
        InputStream stream = statusCode >= 200 && statusCode < 300
                ? connection.getInputStream()
                : connection.getErrorStream();
        String body = readAll(stream);
        connection.disconnect();

        if (statusCode < 200 || statusCode >= 300) {
            throw new IOException("Backend returned HTTP " + statusCode + ": " + body);
        }

        return body;
    }

    private static String readAll(InputStream stream) throws IOException {
        if (stream == null) {
            return "";
        }

        StringBuilder builder = new StringBuilder();
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(stream, StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                builder.append(line);
            }
        }

        return builder.toString();
    }

    private static String trimTrailingSlash(String value) {
        if (value.endsWith("/")) {
            return value.substring(0, value.length() - 1);
        }

        return value;
    }
}
