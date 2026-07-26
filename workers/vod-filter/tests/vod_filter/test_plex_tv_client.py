from __future__ import annotations

import sys
from pathlib import Path


VOD_FILTER_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(VOD_FILTER_ROOT))

from src.clients.plex_tv_client import PlexTvClient, PlexTvShow, VerifiedTvIdentity


class Guid:
    def __init__(self, value: str):
        self.id = value


class Item:
    def __init__(self, item_type: str, key: str, guids: list[str]):
        self.type = item_type
        self.ratingKey = key
        self.guids = [Guid(guid) for guid in guids]
        self.added = False
        self.removed = False

    def addToWatchlist(self) -> None:
        self.added = True

    def removeFromWatchlist(self) -> None:
        self.removed = True


class Library:
    def __init__(self, items: list[Item]):
        self.items = items
        self.requested_name: str | None = None

    def section(self, name: str):
        self.requested_name = name
        return self

    def all(self):
        return self.items


class Server:
    def __init__(self, library_items: list[Item]):
        self.library = Library(library_items)


class Account:
    def __init__(self, watchlist: list[Item], discovery: list[Item]):
        self._watchlist = watchlist
        self._discovery = discovery
        self.discovery_queries: list[dict] = []

    def watchlist(self, **kwargs):
        assert kwargs == {"libtype": "show"}
        return self._watchlist

    def searchDiscover(self, **kwargs):
        self.discovery_queries.append(kwargs)
        return self._discovery


def client(account: Account, library_items: list[Item] = []) -> PlexTvClient:
    return PlexTvClient.from_objects(account=account, server=Server(library_items))


def test_watchlist_and_library_keep_only_show_rows_with_verified_tvdb_guids() -> None:
    show = Item("show", "show-1", ["tvdb://123", "tmdb://9", "imdb://tt0000009"])
    movie = Item("movie", "movie-1", ["tvdb://123"])
    missing_tvdb = Item("show", "show-2", ["tmdb://9"])
    account = Account([show, movie, missing_tvdb], [])
    plex = client(account, [show, movie, missing_tvdb])

    assert plex.get_watchlist_shows() == [
        PlexTvShow(target_id="show-1", identity=VerifiedTvIdentity(123, 9, "tt0000009"))
    ]
    assert plex.get_library_show_identities("TV") == {
        VerifiedTvIdentity(123, 9, "tt0000009")
    }


def test_add_and_remove_authorize_only_an_exact_tvdb_guid() -> None:
    wrong = Item("show", "wrong", ["tvdb://999", "tmdb://9"])
    exact = Item("show", "exact", ["tvdb://123", "tmdb://9"])
    account = Account([], [wrong, exact])
    plex = client(account)
    identity = VerifiedTvIdentity(123, 9, None)

    assert plex.add_watchlist_show(identity) is True
    assert wrong.added is False
    assert exact.added is True
    assert account.discovery_queries == [{"query": "tvdb:123", "limit": 50, "libtype": "show"}]

    account._watchlist = [wrong, exact]
    assert plex.remove_watchlist_show(tvdb_id=123, tmdb_id=9, imdb_id=None) is True
    assert wrong.removed is False
    assert exact.removed is True


def test_watchlist_remove_rejects_a_noncanonical_alternate_guid() -> None:
    tmdb_only = Item("show", "show-1", ["tmdb://9"])

    removed = client(Account([tmdb_only], [])).remove_watchlist_show(
        tvdb_id=123,
        tmdb_id=9,
        imdb_id=None,
    )

    assert removed is False
    assert tmdb_only.removed is False


def test_plex_tv_client_has_no_library_write_surface() -> None:
    forbidden = {"delete_from_library", "delete_episode", "remove_library_item"}

    assert forbidden.isdisjoint(dir(PlexTvClient))
