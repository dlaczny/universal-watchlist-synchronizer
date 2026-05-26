package com.watchlist.tv;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.TextView;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class MainActivity extends Activity {
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final List<Button> filterButtons = new ArrayList<>();

    private WatchlistApiClient apiClient;
    private RemoteImageLoader imageLoader;
    private LinearLayout root;
    private LinearLayout cardsRow;
    private ImageView heroImage;
    private TextView titleView;
    private TextView metaView;
    private TextView statusView;
    private TextView overviewView;
    private TextView messageView;
    private ProgressBar progressBar;
    private String selectedMediaType = WatchlistFilters.MEDIA_MOVIE;
    private String selectedFilter = WatchlistFilters.FILTER_ALL;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        apiClient = new WatchlistApiClient(WatchlistConfig.apiBaseUrl());
        imageLoader = new RemoteImageLoader(executor);
        setContentView(createContentView());
        loadItems();
    }

    @Override
    protected void onDestroy() {
        executor.shutdownNow();
        super.onDestroy();
    }

    private View createContentView() {
        ScrollView scrollView = new ScrollView(this);
        scrollView.setFillViewport(true);

        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(48), dp(36), dp(48), dp(36));
        root.setBackgroundColor(Color.rgb(16, 20, 24));
        scrollView.addView(root, new ScrollView.LayoutParams(
                ScrollView.LayoutParams.MATCH_PARENT,
                ScrollView.LayoutParams.MATCH_PARENT));

        TextView heading = new TextView(this);
        heading.setText("Watchlist");
        heading.setTextColor(Color.WHITE);
        heading.setTextSize(30);
        heading.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(heading);

        root.addView(createControls());
        root.addView(createDetailPanel());

        messageView = new TextView(this);
        messageView.setTextColor(Color.rgb(202, 210, 220));
        messageView.setTextSize(18);
        messageView.setPadding(0, dp(18), 0, dp(12));
        root.addView(messageView);

        progressBar = new ProgressBar(this);
        progressBar.setVisibility(View.GONE);
        root.addView(progressBar);

        HorizontalScrollView horizontalScrollView = new HorizontalScrollView(this);
        horizontalScrollView.setHorizontalScrollBarEnabled(false);
        cardsRow = new LinearLayout(this);
        cardsRow.setOrientation(LinearLayout.HORIZONTAL);
        horizontalScrollView.addView(cardsRow);
        root.addView(horizontalScrollView, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT,
                dp(210)));

        return scrollView;
    }

    private LinearLayout createControls() {
        LinearLayout controls = new LinearLayout(this);
        controls.setOrientation(LinearLayout.HORIZONTAL);
        controls.setGravity(Gravity.CENTER_VERTICAL);
        controls.setPadding(0, dp(24), 0, dp(24));

        controls.addView(filterButton("Movies", WatchlistFilters.MEDIA_MOVIE, null));
        controls.addView(filterButton("TV Shows", WatchlistFilters.MEDIA_TV, null));
        controls.addView(spacer(dp(24), 1));
        controls.addView(filterButton("All", null, WatchlistFilters.FILTER_ALL));
        controls.addView(filterButton("Available", null, WatchlistFilters.FILTER_AVAILABLE));

        updateFilterButtonStyles();
        return controls;
    }

    private Button filterButton(String text, String mediaType, String filter) {
        Button button = new Button(this);
        button.setText(text);
        button.setAllCaps(false);
        button.setTextSize(16);
        button.setMinWidth(dp(132));
        button.setFocusable(true);
        button.setOnClickListener(view -> {
            if (mediaType != null) {
                selectedMediaType = mediaType;
            }
            if (filter != null) {
                selectedFilter = filter;
            }
            updateFilterButtonStyles();
            loadItems();
        });

        filterButtons.add(button);
        return button;
    }

    private LinearLayout createDetailPanel() {
        LinearLayout detail = new LinearLayout(this);
        detail.setOrientation(LinearLayout.HORIZONTAL);
        detail.setGravity(Gravity.CENTER_VERTICAL);
        detail.setPadding(0, 0, 0, dp(24));

        heroImage = new ImageView(this);
        heroImage.setScaleType(ImageView.ScaleType.CENTER_CROP);
        detail.addView(heroImage, new LinearLayout.LayoutParams(dp(220), dp(330)));

        LinearLayout textColumn = new LinearLayout(this);
        textColumn.setOrientation(LinearLayout.VERTICAL);
        textColumn.setPadding(dp(32), 0, 0, 0);

        titleView = detailText(34, true);
        metaView = detailText(18, false);
        statusView = detailText(18, true);
        overviewView = detailText(20, false);
        overviewView.setMaxLines(5);

        textColumn.addView(titleView);
        textColumn.addView(metaView);
        textColumn.addView(statusView);
        textColumn.addView(overviewView);
        detail.addView(textColumn, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));

        return detail;
    }

    private TextView detailText(int sizeSp, boolean bold) {
        TextView textView = new TextView(this);
        textView.setTextColor(Color.WHITE);
        textView.setTextSize(sizeSp);
        textView.setPadding(0, 0, 0, dp(10));
        if (bold) {
            textView.setTypeface(Typeface.DEFAULT_BOLD);
        }
        return textView;
    }

    private void loadItems() {
        showLoading();
        executor.execute(() -> {
            try {
                List<WatchlistItem> items = apiClient.getWatchlist(selectedMediaType, selectedFilter);
                mainHandler.post(() -> renderItems(items));
            } catch (Exception exception) {
                mainHandler.post(() -> showError(exception));
            }
        });
    }

    private void renderItems(List<WatchlistItem> items) {
        progressBar.setVisibility(View.GONE);
        cardsRow.removeAllViews();

        if (items.isEmpty()) {
            messageView.setText("No items match this filter.");
            renderDetail(null);
            return;
        }

        messageView.setText("");
        for (WatchlistItem item : items) {
            TextView card = createCard(item);
            cardsRow.addView(card);
        }

        cardsRow.getChildAt(0).requestFocus();
        renderDetail(items.get(0));
    }

    private TextView createCard(WatchlistItem item) {
        TextView card = new TextView(this);
        card.setText(item.title());
        card.setTextColor(Color.WHITE);
        card.setTextSize(16);
        card.setGravity(Gravity.BOTTOM | Gravity.CENTER_HORIZONTAL);
        card.setPadding(dp(10), dp(10), dp(10), dp(14));
        card.setFocusable(true);
        card.setBackground(cardBackground(false));
        card.setOnFocusChangeListener((view, hasFocus) -> {
            view.setBackground(cardBackground(hasFocus));
            if (hasFocus) {
                renderDetail(item);
            }
        });
        card.setOnClickListener(view -> renderDetail(item));

        LinearLayout.LayoutParams layoutParams = new LinearLayout.LayoutParams(dp(150), dp(190));
        layoutParams.setMargins(0, 0, dp(16), 0);
        card.setLayoutParams(layoutParams);

        return card;
    }

    private void renderDetail(WatchlistItem item) {
        if (item == null) {
            titleView.setText("Nothing here yet");
            metaView.setText("");
            statusView.setText("");
            overviewView.setText("");
            heroImage.setImageBitmap(null);
            heroImage.setBackgroundColor(Color.rgb(42, 48, 56));
            return;
        }

        titleView.setText(item.title());
        metaView.setText(formatMeta(item));
        statusView.setText(formatAvailability(item));
        overviewView.setText(item.overview() == null ? "" : item.overview());
        imageLoader.load(heroImage, item.posterUrl(), Color.rgb(42, 48, 56));
    }

    private void showLoading() {
        progressBar.setVisibility(View.VISIBLE);
        messageView.setText("Loading watchlist...");
        cardsRow.removeAllViews();
    }

    private void showError(Exception exception) {
        progressBar.setVisibility(View.GONE);
        cardsRow.removeAllViews();
        renderDetail(null);
        messageView.setText("Could not load watchlist from " + WatchlistConfig.apiBaseUrl()
                + ". " + exception.getMessage());
    }

    private void updateFilterButtonStyles() {
        for (Button button : filterButtons) {
            String text = button.getText().toString();
            boolean selected = ("Movies".equals(text) && WatchlistFilters.MEDIA_MOVIE.equals(selectedMediaType))
                    || ("TV Shows".equals(text) && WatchlistFilters.MEDIA_TV.equals(selectedMediaType))
                    || ("All".equals(text) && WatchlistFilters.FILTER_ALL.equals(selectedFilter))
                    || ("Available".equals(text) && WatchlistFilters.FILTER_AVAILABLE.equals(selectedFilter));
            button.setTextColor(selected ? Color.rgb(16, 20, 24) : Color.WHITE);
            button.setBackgroundColor(selected ? Color.rgb(56, 189, 248) : Color.rgb(42, 48, 56));
        }
    }

    private static String formatMeta(WatchlistItem item) {
        String type = "movie".equals(item.mediaType()) ? "Movie" : "TV Show";
        return item.year() == null ? type : type + " - " + item.year();
    }

    private static String formatAvailability(WatchlistItem item) {
        if (WatchlistFilters.AVAILABLE_ON_PLEX.equals(item.availabilityStatus())) {
            return "Available on Plex";
        }
        if ("unreleased".equals(item.availabilityStatus())) {
            return "Unreleased";
        }
        if ("unknown_match".equals(item.availabilityStatus())) {
            return "Plex match uncertain";
        }
        return "Not on Plex";
    }

    private GradientDrawable cardBackground(boolean focused) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setColor(focused ? Color.rgb(56, 189, 248) : Color.rgb(31, 41, 55));
        drawable.setCornerRadius(dp(6));
        drawable.setStroke(dp(focused ? 4 : 1), focused ? Color.WHITE : Color.rgb(75, 85, 99));
        return drawable;
    }

    private View spacer(int width, int height) {
        View view = new View(this);
        view.setLayoutParams(new LinearLayout.LayoutParams(width, height));
        return view;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density);
    }
}
