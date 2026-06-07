package com.watchlist.tv;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.RejectedExecutionException;

public final class DetailsActivity extends Activity {
    public static final String EXTRA_ITEM = "com.watchlist.tv.ITEM";

    private final ExecutorService apiExecutor = Executors.newSingleThreadExecutor();
    private final ExecutorService imageExecutor = Executors.newFixedThreadPool(2);
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private WatchlistApiClient apiClient;
    private RemoteImageLoader imageLoader;
    private WatchlistItemDetails currentDetails;
    private FrameLayout root;
    private ImageView backdropView;
    private ImageView posterView;
    private TextView missingPosterView;
    private TextView titleView;
    private TextView metadataView;
    private TextView membershipView;
    private TextView overviewView;
    private TextView errorView;
    private Button primaryActionButton;
    private volatile boolean destroyed;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        WatchlistItem item = (WatchlistItem) getIntent().getSerializableExtra(EXTRA_ITEM);
        if (item == null) {
            finish();
            return;
        }

        apiClient = new WatchlistApiClient(WatchlistConfig.apiBaseUrl());
        imageLoader = new RemoteImageLoader(imageExecutor, () -> !destroyed);
        currentDetails = WatchlistItemDetails.fromItem(item);
        setContentView(createContentView());
        render(currentDetails);
        primaryActionButton.requestFocus();
        fetchDetails(item.id());
    }

    @Override
    protected void onDestroy() {
        destroyed = true;
        mainHandler.removeCallbacksAndMessages(null);
        imageLoader.discardObsoleteRequests();
        apiExecutor.shutdownNow();
        imageExecutor.shutdownNow();
        super.onDestroy();
    }

    private View createContentView() {
        root = new FrameLayout(this);
        root.setBackgroundColor(Color.rgb(15, 20, 25));

        backdropView = new ImageView(this);
        backdropView.setScaleType(ImageView.ScaleType.CENTER_CROP);
        root.addView(backdropView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        View scrim = new View(this);
        scrim.setBackgroundColor(Color.argb(205, 10, 14, 18));
        root.addView(scrim, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.HORIZONTAL);
        content.setGravity(Gravity.CENTER_VERTICAL);
        content.setPadding(dp(72), dp(54), dp(72), dp(54));
        root.addView(content, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        FrameLayout posterFrame = new FrameLayout(this);
        posterFrame.setBackgroundColor(Color.rgb(42, 48, 56));
        content.addView(posterFrame, new LinearLayout.LayoutParams(dp(230), dp(345)));

        posterView = new ImageView(this);
        posterView.setScaleType(ImageView.ScaleType.CENTER_CROP);
        posterFrame.addView(posterView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        missingPosterView = new TextView(this);
        missingPosterView.setText(R.string.message_artwork_unavailable);
        missingPosterView.setTextColor(Color.rgb(203, 213, 225));
        missingPosterView.setTextSize(18);
        missingPosterView.setGravity(Gravity.CENTER);
        posterFrame.addView(missingPosterView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        LinearLayout details = new LinearLayout(this);
        details.setOrientation(LinearLayout.VERTICAL);
        details.setPadding(dp(42), 0, 0, 0);
        content.addView(details, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));

        titleView = new TextView(this);
        titleView.setTextColor(Color.WHITE);
        titleView.setTextSize(38);
        titleView.setTypeface(Typeface.DEFAULT_BOLD);
        titleView.setMaxLines(2);
        titleView.setEllipsize(TextUtils.TruncateAt.END);
        details.addView(titleView);

        metadataView = new TextView(this);
        metadataView.setTextColor(Color.rgb(203, 213, 225));
        metadataView.setTextSize(18);
        metadataView.setPadding(0, dp(12), 0, 0);
        details.addView(metadataView);

        membershipView = new TextView(this);
        membershipView.setTextColor(Color.rgb(203, 213, 225));
        membershipView.setTextSize(17);
        membershipView.setPadding(0, dp(14), 0, 0);
        details.addView(membershipView);

        primaryActionButton = new Button(this);
        primaryActionButton.setAllCaps(false);
        primaryActionButton.setTextSize(18);
        primaryActionButton.setMinHeight(dp(54));
        primaryActionButton.setOnClickListener(view -> view.requestFocus());
        LinearLayout.LayoutParams actionParams = new LinearLayout.LayoutParams(dp(220), dp(58));
        actionParams.setMargins(0, dp(26), 0, dp(24));
        details.addView(primaryActionButton, actionParams);

        overviewView = new TextView(this);
        overviewView.setTextColor(Color.WHITE);
        overviewView.setTextSize(18);
        overviewView.setLineSpacing(0, 1.1f);
        overviewView.setMaxLines(5);
        overviewView.setEllipsize(TextUtils.TruncateAt.END);
        details.addView(overviewView);

        errorView = new TextView(this);
        errorView.setTextColor(Color.rgb(252, 165, 165));
        errorView.setTextSize(15);
        errorView.setPadding(0, dp(18), 0, 0);
        details.addView(errorView);

        return root;
    }

    private void fetchDetails(String id) {
        try {
            apiExecutor.execute(() -> {
                try {
                    WatchlistItemDetails details = apiClient.getWatchlistItemDetails(id);
                    mainHandler.post(() -> {
                        if (!destroyed) {
                            currentDetails = details;
                            render(details);
                        }
                    });
                } catch (Exception exception) {
                    mainHandler.post(() -> {
                        if (!destroyed) {
                            errorView.setText(getString(R.string.message_detail_backend_error, exception.getMessage()));
                        }
                    });
                }
            });
        } catch (RejectedExecutionException ignored) {
            // The Activity is tearing down.
        }
    }

    private void render(WatchlistItemDetails details) {
        titleView.setText(details.title());
        String metadata = details.metadataSummary();
        metadataView.setText(metadata);
        metadataView.setVisibility(metadata.isEmpty() ? View.GONE : View.VISIBLE);
        membershipView.setText(details.isPlexOnly() ? getString(R.string.message_plex_only_detail) : "");
        membershipView.setVisibility(details.isPlexOnly() ? View.VISIBLE : View.GONE);
        overviewView.setText(details.overview() == null || details.overview().isEmpty()
                ? getString(R.string.message_no_description)
                : details.overview());
        primaryActionButton.setText(details.primaryActionLabel());
        primaryActionButton.setEnabled(details.primaryActionEnabled());
        primaryActionButton.setTextColor(details.primaryActionEnabled() ? Color.rgb(15, 20, 25) : Color.rgb(203, 213, 225));
        primaryActionButton.setBackground(actionBackground(details.primaryActionEnabled(), primaryActionButton.hasFocus()));
        primaryActionButton.setOnFocusChangeListener((view, hasFocus) ->
                view.setBackground(actionBackground(details.primaryActionEnabled(), hasFocus)));

        imageLoader.load(backdropView, details.backdropUrl(), Color.rgb(15, 20, 25), loaded -> {});
        missingPosterView.setVisibility(details.posterUrl() == null || details.posterUrl().isEmpty()
                ? View.VISIBLE
                : View.GONE);
        imageLoader.load(posterView, details.posterUrl(), Color.rgb(42, 48, 56), loaded ->
                missingPosterView.setVisibility(loaded ? View.GONE : View.VISIBLE));
    }

    private GradientDrawable actionBackground(boolean enabled, boolean focused) {
        GradientDrawable drawable = new GradientDrawable();
        drawable.setCornerRadius(dp(5));
        drawable.setColor(enabled ? Color.rgb(103, 232, 249) : Color.rgb(43, 53, 64));
        drawable.setStroke(dp(focused ? 3 : 1), focused ? Color.WHITE : Color.rgb(75, 85, 99));
        return drawable;
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density);
    }
}
