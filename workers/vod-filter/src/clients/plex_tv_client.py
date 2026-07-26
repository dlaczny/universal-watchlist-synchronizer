"""GUID-only Plex TV watchlist and library-read boundary client."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any

from plexapi.myplex import MyPlexAccount
from plexapi.server import PlexServer


class PlexTvError(RuntimeError):
    """Raised when Plex cannot complete an exact-identity TV operation."""


@dataclass(frozen=True)
class VerifiedTvIdentity:
    """A backend-verifiable TV identity, anchored by a positive TVDB ID."""

    tvdb_id: int
    tmdb_id: int | None
    imdb_id: str | None

    def __post_init__(self) -> None:
        if not _is_positive_id(self.tvdb_id):
            raise PlexTvError("Plex TVDB ID must be a positive integer")
        if self.tmdb_id is not None and not _is_positive_id(self.tmdb_id):
            raise PlexTvError("Plex TMDB ID must be a positive integer")
        if self.imdb_id is not None and not _is_imdb_id(self.imdb_id):
            raise PlexTvError("Plex IMDb ID is invalid")


@dataclass(frozen=True)
class PlexTvShow:
    """A universal-watchlist show with an exact GUID identity."""

    target_id: str
    identity: VerifiedTvIdentity


class PlexTvClient:
    """Operate on Plex universal watchlist rows; never mutate a Plex library."""

    def __init__(self, url: str, token: str) -> None:
        try:
            self.server = PlexServer(url, token)
            self.account = MyPlexAccount(token=token)
        except Exception as error:
            raise PlexTvError("Plex TV client initialization failed") from error

    @classmethod
    def from_objects(cls, *, account: Any, server: Any) -> "PlexTvClient":
        """Construct a client around existing Plex objects for isolated tests."""
        instance = cls.__new__(cls)
        instance.account = account
        instance.server = server
        return instance

    def get_watchlist_shows(self) -> list[PlexTvShow]:
        """List show watchlist rows that have a canonical, verified TVDB GUID."""
        try:
            rows = self.account.watchlist(libtype="show")
        except Exception as error:
            raise PlexTvError("Plex TV watchlist read failed") from error
        return self._show_rows(rows, "watchlist")

    def get_library_show_identities(self, library_name: str) -> set[VerifiedTvIdentity]:
        """Read exact show identities from a Plex library without writing to it."""
        if not isinstance(library_name, str) or not library_name.strip():
            raise PlexTvError("Plex TV library name is required")
        try:
            rows = self.server.library.section(library_name).all()
        except Exception as error:
            raise PlexTvError("Plex TV library read failed") from error
        return {row.identity for row in self._show_rows(rows, "library")}

    def add_watchlist_show(self, identity: VerifiedTvIdentity) -> bool:
        """Add one discovered show only when its canonical TVDB GUID is exact."""
        self._require_identity(identity)
        existing = [
            row
            for row in self.get_watchlist_shows()
            if row.identity.tvdb_id == identity.tvdb_id
        ]
        if existing:
            return self._identities_compatible(existing[0].identity, identity)
        try:
            candidates = self.account.searchDiscover(
                query=f"tvdb:{identity.tvdb_id}",
                limit=50,
                libtype="show",
            )
        except Exception as error:
            raise PlexTvError(f"Plex TV discovery failed for TVDB {identity.tvdb_id}") from error
        matches = self._identity_matches(candidates, identity)
        if not matches:
            return False
        if len(matches) != 1:
            raise PlexTvError(f"Plex TV discovery is ambiguous for TVDB {identity.tvdb_id}")
        try:
            matches[0].addToWatchlist()
        except Exception as error:
            raise PlexTvError(f"Plex TV watchlist add failed for TVDB {identity.tvdb_id}") from error
        return True

    def remove_watchlist_show(
        self,
        tvdb_id: int,
        tmdb_id: int | None,
        imdb_id: str | None,
    ) -> bool:
        """Remove at most one universal-watchlist show by its canonical TVDB GUID."""
        identity = VerifiedTvIdentity(tvdb_id=tvdb_id, tmdb_id=tmdb_id, imdb_id=imdb_id)
        matches = [
            row
            for row in self.get_watchlist_shows()
            if self._identities_compatible(row.identity, identity)
        ]
        if not matches:
            return False
        if len(matches) != 1:
            raise PlexTvError(f"Plex TV watchlist is ambiguous for TVDB {identity.tvdb_id}")
        try:
            target = self._watchlist_item_by_target_id(matches[0].target_id, identity)
            if target is None:
                return False
            target.removeFromWatchlist()
        except PlexTvError:
            raise
        except Exception as error:
            raise PlexTvError(f"Plex TV watchlist remove failed for TVDB {identity.tvdb_id}") from error
        return True

    def _watchlist_item_by_target_id(
        self,
        target_id: str,
        expected_identity: VerifiedTvIdentity,
    ) -> Any | None:
        try:
            rows = self.account.watchlist(libtype="show")
        except Exception as error:
            raise PlexTvError("Plex TV watchlist read failed") from error
        matches = [item for item in rows if self._target_id(item) == target_id]
        if len(matches) != 1:
            return None
        target = matches[0]
        if getattr(target, "type", None) != "show":
            return None
        current_identity = self._identity_from_item(target)
        if current_identity is None or not self._identities_compatible(
            current_identity, expected_identity
        ):
            return None
        return target

    @classmethod
    def _show_rows(cls, rows: Any, source: str) -> list[PlexTvShow]:
        if not isinstance(rows, (list, tuple)):
            raise PlexTvError(f"Plex TV {source} returned a non-list payload")
        result: list[PlexTvShow] = []
        seen_tvdb_ids: set[int] = set()
        for item in rows:
            if getattr(item, "type", None) != "show":
                continue
            identity = cls._identity_from_item(item)
            if identity is None:
                continue
            target_id = cls._target_id(item)
            if target_id is None:
                continue
            if identity.tvdb_id in seen_tvdb_ids:
                raise PlexTvError(f"Plex TV {source} has duplicate TVDB {identity.tvdb_id}")
            seen_tvdb_ids.add(identity.tvdb_id)
            result.append(PlexTvShow(target_id=target_id, identity=identity))
        return result

    @classmethod
    def _identity_matches(
        cls,
        candidates: Any,
        expected_identity: VerifiedTvIdentity,
    ) -> list[Any]:
        if not isinstance(candidates, (list, tuple)):
            raise PlexTvError("Plex TV discovery returned a non-list payload")
        return [
            item
            for item in candidates
            if getattr(item, "type", None) == "show"
            and (candidate_identity := cls._identity_from_item(item)) is not None
            and cls._identities_compatible(candidate_identity, expected_identity)
        ]

    @staticmethod
    def _identities_compatible(
        actual: VerifiedTvIdentity,
        expected: VerifiedTvIdentity,
    ) -> bool:
        return (
            actual.tvdb_id == expected.tvdb_id
            and (expected.tmdb_id is None or actual.tmdb_id in (None, expected.tmdb_id))
            and (expected.imdb_id is None or actual.imdb_id in (None, expected.imdb_id))
        )

    @staticmethod
    def _require_identity(identity: Any) -> VerifiedTvIdentity:
        if not isinstance(identity, VerifiedTvIdentity):
            raise PlexTvError("Plex TV operation requires a verified identity")
        return identity

    @staticmethod
    def _target_id(item: Any) -> str | None:
        for attribute in ("ratingKey", "key"):
            value = getattr(item, attribute, None)
            if value is not None and str(value).strip():
                return str(value)
        return None

    @classmethod
    def _identity_from_item(cls, item: Any) -> VerifiedTvIdentity | None:
        values: dict[str, str] = {}
        for guid in getattr(item, "guids", ()):
            raw = getattr(guid, "id", guid)
            if not isinstance(raw, str):
                continue
            kind, separator, value = raw.partition("://")
            if not separator:
                continue
            kind = kind.lower()
            value = value.split("?", 1)[0].split("#", 1)[0].strip()
            if kind not in {"tvdb", "tmdb", "imdb"}:
                continue
            previous = values.get(kind)
            if previous is not None and previous != value:
                return None
            values[kind] = value
        tvdb_raw = values.get("tvdb")
        if tvdb_raw is None or not tvdb_raw.isdigit() or not _is_positive_id(int(tvdb_raw)):
            return None
        tmdb_raw = values.get("tmdb")
        if tmdb_raw is not None and (not tmdb_raw.isdigit() or not _is_positive_id(int(tmdb_raw))):
            return None
        imdb_raw = values.get("imdb")
        if imdb_raw is not None and not _is_imdb_id(imdb_raw):
            return None
        return VerifiedTvIdentity(
            tvdb_id=int(tvdb_raw),
            tmdb_id=int(tmdb_raw) if tmdb_raw is not None else None,
            imdb_id=imdb_raw,
        )


def _is_positive_id(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool) and value > 0


def _is_imdb_id(value: Any) -> bool:
    return (
        isinstance(value, str)
        and value.startswith("tt")
        and value[2:].isdigit()
        and int(value[2:]) > 0
    )
