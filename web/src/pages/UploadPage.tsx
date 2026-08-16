import { useEffect, useRef, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { discoveryApi, tracksApi } from '../services/api';
import { ApiError, apiUrl } from '../services/apiClient';
import { readTokens } from '../services/tokenStore';
import { ErrorMessage, Loading } from '../components/common';
import { formatBytes } from '../lib/format';
import type { ContentVisibility, UploadAccepted } from '../types/api';

/** Taille maximale acceptée, alignée sur la contrainte du backend. */
const MAX_FILE_BYTES = 20 * 1024 * 1024;

const ACCEPTED = '.mp3,.m4a,.aac,.flac,.ogg,.oga,.opus,.wav';

/** Intervalle d'interrogation de l'état de traitement, en millisecondes. */
const POLL_INTERVAL_MS = 1500;

/** Origine du morceau importé : fichier local ou lien YouTube. */
type ImportSource = 'file' | 'youtube';

/**
 * Import d'un morceau, depuis un fichier local ou depuis un lien YouTube.
 *
 * L'envoi d'un fichier utilise `XMLHttpRequest` plutôt que `fetch` : c'est la seule API
 * du navigateur qui expose la progression de l'upload, indispensable pour un fichier
 * pouvant atteindre 20 Mo. L'import par lien est en revanche une simple requête JSON :
 * le téléchargement est entièrement réalisé par le serveur.
 */
export function UploadPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: genres } = useQuery({ queryKey: ['genres'], queryFn: discoveryApi.genres, staleTime: 300000 });

  const [source, setSource] = useState<ImportSource>('file');
  const [file, setFile] = useState<File | null>(null);
  const [youtubeUrl, setYoutubeUrl] = useState('');
  const [title, setTitle] = useState('');
  const [artistName, setArtistName] = useState('');
  const [description, setDescription] = useState('');
  const [genreId, setGenreId] = useState('');
  const [year, setYear] = useState('');
  const [visibility, setVisibility] = useState<ContentVisibility>('Public');
  const [tags, setTags] = useState('');
  const [cover, setCover] = useState<File | null>(null);

  const [progress, setProgress] = useState(0);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [accepted, setAccepted] = useState<UploadAccepted | null>(null);

  /** Vérifie la source choisie et retourne le message à afficher, ou null. */
  function validate(): string | null {
    if (source === 'youtube') {
      return isYoutubeUrl(youtubeUrl) ? null : 'Saisissez un lien de vidéo YouTube valide.';
    }
    if (!file) {
      return 'Sélectionnez un fichier audio.';
    }
    if (file.size > MAX_FILE_BYTES) {
      return `Le fichier dépasse la taille maximale de ${formatBytes(MAX_FILE_BYTES)}.`;
    }
    return null;
  }

  /** Envoie le fichier local et retourne l'accusé de réception. */
  function submitFile(): Promise<UploadAccepted> {
    const form = new FormData();
    form.append('file', file!);
    form.append('visibility', visibility);
    if (title.trim()) form.append('title', title.trim());
    if (artistName.trim()) form.append('artistName', artistName.trim());
    if (description.trim()) form.append('description', description.trim());
    if (genreId) form.append('genreId', genreId);
    if (year) form.append('year', year);

    for (const tag of parseTags(tags)) {
      form.append('tags', tag);
    }

    return uploadWithProgress(form, setProgress);
  }

  /** Demande au serveur de télécharger la vidéo et retourne l'accusé de réception. */
  function submitYoutubeLink(): Promise<UploadAccepted> {
    return tracksApi.importFromYoutube({
      url: youtubeUrl.trim(),
      visibility,
      title: title.trim() || undefined,
      artistName: artistName.trim() || undefined,
      description: description.trim() || undefined,
      genreId: genreId || undefined,
      year: year ? Number(year) : undefined,
      tags: parseTags(tags),
    });
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    const validationMessage = validate();
    if (validationMessage) {
      setError(new ApiError(400, { detail: validationMessage }));
      return;
    }

    setUploading(true);
    setProgress(0);

    try {
      const result = source === 'youtube' ? await submitYoutubeLink() : await submitFile();
      setAccepted(result);

      // Une pochette fournie explicitement remplace celle déduite du fichier ou de la vidéo.
      if (cover) {
        await tracksApi.setCover(result.trackId, cover);
      }

      queryClient.invalidateQueries({ queryKey: ['my-tracks'] });
    } catch (caught) {
      setError(caught);
      setUploading(false);
    }
  }

  const isSubmitDisabled = uploading || (source === 'file' ? !file : youtubeUrl.trim().length === 0);

  return (
    <>
      <h1>Importer un morceau</h1>

      {accepted ? (
        <ProcessingStatus trackId={accepted.trackId} onReady={() => navigate(`/tracks/${accepted.trackId}`)} />
      ) : (
        <form onSubmit={handleSubmit} className="card" style={{ maxWidth: 620 }}>
          <ErrorMessage error={error} />

          <fieldset className="field">
            <legend>Source du morceau</legend>
            <div className="row">
              <label>
                <input
                  type="radio"
                  name="upload-source"
                  value="file"
                  checked={source === 'file'}
                  onChange={() => setSource('file')}
                />{' '}
                Fichier audio
              </label>
              <label>
                <input
                  type="radio"
                  name="upload-source"
                  value="youtube"
                  checked={source === 'youtube'}
                  onChange={() => setSource('youtube')}
                />{' '}
                Lien YouTube
              </label>
            </div>
          </fieldset>

          {source === 'file' ? (
            <div className="field">
              <label htmlFor="upload-file">Fichier audio</label>
              <input
                id="upload-file"
                type="file"
                accept={ACCEPTED}
                required
                onChange={(event) => setFile(event.target.files?.[0] ?? null)}
                aria-describedby="upload-file-help"
              />
              <span id="upload-file-help" className="small muted">
                Formats acceptés : MP3, M4A, AAC, FLAC, OGG, Opus, WAV. Taille maximale {formatBytes(MAX_FILE_BYTES)}.
                {file && ` Fichier sélectionné : ${file.name} (${formatBytes(file.size)}).`}
              </span>
            </div>
          ) : (
            <div className="field">
              <label htmlFor="upload-youtube">Lien de la vidéo</label>
              <input
                id="upload-youtube"
                type="url"
                required
                value={youtubeUrl}
                onChange={(event) => setYoutubeUrl(event.target.value)}
                placeholder="https://www.youtube.com/watch?v=…"
                aria-describedby="upload-youtube-help"
              />
              <span id="upload-youtube-help" className="small muted">
                Le serveur télécharge la piste audio en MP3 et utilise la miniature de la vidéo comme
                pochette. Titre et artiste sont repris de la vidéo si vous les laissez vides. Assurez-vous
                de disposer des droits nécessaires sur le contenu importé.
              </span>
            </div>
          )}

          <div className="field">
            <label htmlFor="upload-title">Titre</label>
            <input
              id="upload-title"
              value={title}
              maxLength={200}
              onChange={(event) => setTitle(event.target.value)}
              placeholder="Laissé vide, le nom du fichier sera utilisé"
            />
          </div>

          <div className="field">
            <label htmlFor="upload-artist">Nom d'artiste</label>
            <input
              id="upload-artist"
              value={artistName}
              maxLength={200}
              onChange={(event) => setArtistName(event.target.value)}
              placeholder="Par défaut, votre nom d'utilisateur"
            />
          </div>

          <div className="field">
            <label htmlFor="upload-description">Description</label>
            <textarea
              id="upload-description"
              value={description}
              maxLength={5000}
              onChange={(event) => setDescription(event.target.value)}
            />
          </div>

          <div className="row wrap">
            <div className="field grow">
              <label htmlFor="upload-genre">Genre</label>
              <select id="upload-genre" value={genreId} onChange={(event) => setGenreId(event.target.value)}>
                <option value="">Non précisé</option>
                {genres?.map((genre) => (
                  <option key={genre.id} value={genre.id}>
                    {genre.name}
                  </option>
                ))}
              </select>
            </div>

            <div className="field" style={{ width: 120 }}>
              <label htmlFor="upload-year">Année</label>
              <input
                id="upload-year"
                type="number"
                min={1900}
                max={2999}
                value={year}
                onChange={(event) => setYear(event.target.value)}
              />
            </div>

            <div className="field grow">
              <label htmlFor="upload-visibility">Visibilité</label>
              <select
                id="upload-visibility"
                value={visibility}
                onChange={(event) => setVisibility(event.target.value as ContentVisibility)}
              >
                <option value="Public">Public</option>
                <option value="Unlisted">Non répertorié (accessible par lien)</option>
                <option value="Private">Privé</option>
              </select>
            </div>
          </div>

          <div className="field">
            <label htmlFor="upload-tags">Tags</label>
            <input
              id="upload-tags"
              value={tags}
              onChange={(event) => setTags(event.target.value)}
              placeholder="#rock #indie électro"
              aria-describedby="upload-tags-help"
            />
            <span id="upload-tags-help" className="small muted">
              Séparés par des espaces ou des virgules. Le préfixe # est facultatif.
            </span>
          </div>

          <div className="field">
            <label htmlFor="upload-cover">Pochette personnalisée (facultatif)</label>
            <input
              id="upload-cover"
              type="file"
              accept="image/*"
              onChange={(event) => setCover(event.target.files?.[0] ?? null)}
              aria-describedby="upload-cover-help"
            />
            <span id="upload-cover-help" className="small muted">
              {source === 'youtube'
                ? 'Sans pochette fournie, la miniature de la vidéo sera utilisée.'
                : 'Sans pochette fournie, celle intégrée au fichier audio sera utilisée.'}
            </span>
          </div>

          {uploading && source === 'file' && (
            <div className="field">
              <label htmlFor="upload-progress">Envoi en cours</label>
              <div className="progress-bar" id="upload-progress" role="progressbar" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
                <div style={{ width: `${progress}%` }} />
              </div>
              <span className="small muted">{progress} %</span>
            </div>
          )}

          {uploading && source === 'youtube' && (
            <p className="small muted" role="status">
              Téléchargement de la vidéo par le serveur… Cette étape peut prendre une minute.
            </p>
          )}

          <button type="submit" className="btn btn-primary" disabled={isSubmitDisabled}>
            {uploading ? 'Envoi…' : 'Importer'}
          </button>
        </form>
      )}
    </>
  );
}

/**
 * Suit l'état de traitement du morceau jusqu'à ce qu'il soit prêt ou en échec.
 * L'interrogation s'arrête dès qu'un état terminal est atteint.
 */
function ProcessingStatus({ trackId, onReady }: { trackId: string; onReady: () => void }) {
  const { data, error } = useQuery({
    queryKey: ['track', trackId],
    queryFn: () => tracksApi.get(trackId),
    refetchInterval: (query) => {
      const status = query.state.data?.track.status;
      return status === 'Ready' || status === 'Failed' ? false : POLL_INTERVAL_MS;
    },
  });

  const status = data?.track.status;

  useEffect(() => {
    if (status === 'Ready') {
      const timer = setTimeout(onReady, 800);
      return () => clearTimeout(timer);
    }
  }, [status, onReady]);

  if (error) {
    return <ErrorMessage error={error} />;
  }

  if (status === 'Failed') {
    return (
      <div className="alert alert-error" role="alert">
        Le traitement du fichier a échoué : {data?.failureReason ?? 'fichier audio illisible.'}
      </div>
    );
  }

  if (status === 'Ready') {
    return (
      <div className="alert alert-success" role="status">
        Le morceau est prêt. Redirection en cours…
      </div>
    );
  }

  return (
    <div className="card" style={{ maxWidth: 620 }}>
      <p role="status">Fichier reçu. Analyse des métadonnées et génération des pochettes en cours…</p>
      <Loading rows={1} />
    </div>
  );
}

/**
 * Envoie le formulaire multipart en publiant la progression.
 * Le jeton est posé manuellement car la requête ne passe pas par le client `fetch`.
 */
function uploadWithProgress(form: FormData, onProgress: (percent: number) => void): Promise<UploadAccepted> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', apiUrl('/tracks'));

    const token = readTokens()?.accessToken;
    if (token) {
      xhr.setRequestHeader('Authorization', `Bearer ${token}`);
    }

    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) {
        onProgress(Math.round((event.loaded / event.total) * 100));
      }
    };

    xhr.onload = () => {
      const payload = xhr.responseText ? JSON.parse(xhr.responseText) : null;
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(payload as UploadAccepted);
      } else {
        reject(new ApiError(xhr.status, payload ?? { status: xhr.status }));
      }
    };

    xhr.onerror = () => reject(new ApiError(0, { detail: "L'envoi a échoué. Vérifiez votre connexion." }));
    xhr.send(form);
  });
}

/**
 * Vérifie sommairement qu'une saisie ressemble à un lien de vidéo YouTube.
 * La validation qui fait foi reste celle du serveur ; ce contrôle évite seulement
 * un aller-retour réseau inutile.
 */
function isYoutubeUrl(raw: string): boolean {
  try {
    const url = new URL(raw.trim());
    const host = url.hostname.replace(/^www\./, '');
    return host === 'youtu.be' || host === 'youtube.com' || host === 'm.youtube.com' || host === 'music.youtube.com';
  } catch {
    return false;
  }
}

/** Découpe une saisie libre de tags en libellés individuels. */
function parseTags(raw: string): string[] {
  return raw
    .split(/[\s,]+/)
    .map((tag) => tag.replace(/^#/, '').trim())
    .filter((tag) => tag.length > 0)
    .slice(0, 20);
}

/** Édition des métadonnées d'un morceau existant. */
export function EditTrackPage() {
  const { trackId = '' } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const coverInputRef = useRef<HTMLInputElement>(null);

  const { data: genres } = useQuery({ queryKey: ['genres'], queryFn: discoveryApi.genres, staleTime: 300000 });
  const { data, isLoading } = useQuery({ queryKey: ['track', trackId], queryFn: () => tracksApi.get(trackId) });

  const [form, setForm] = useState<{
    title: string;
    artistName: string;
    description: string;
    genreId: string;
    year: string;
    visibility: ContentVisibility;
    tags: string;
  } | null>(null);

  // Le formulaire est initialisé une seule fois, à l'arrivée des données.
  useEffect(() => {
    if (data && !form) {
      setForm({
        title: data.track.title,
        artistName: data.track.artistName,
        description: data.description ?? '',
        genreId: data.track.genre?.id ?? '',
        year: data.year ? String(data.year) : '',
        visibility: data.track.visibility,
        tags: data.track.tags.map((tag) => `#${tag}`).join(' '),
      });
    }
  }, [data, form]);

  const updateMutation = useMutation({
    mutationFn: () =>
      tracksApi.update(trackId, {
        title: form!.title,
        artistName: form!.artistName,
        description: form!.description,
        genreId: form!.genreId || undefined,
        clearGenre: !form!.genreId,
        year: form!.year ? Number(form!.year) : undefined,
        visibility: form!.visibility,
        tags: parseTags(form!.tags),
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['track', trackId] });
      navigate(`/tracks/${trackId}`);
    },
  });

  const coverMutation = useMutation({
    mutationFn: (file: File) => tracksApi.setCover(trackId, file),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['track', trackId] }),
  });

  const deleteMutation = useMutation({
    mutationFn: () => tracksApi.remove(trackId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['my-tracks'] });
      navigate('/me/tracks');
    },
  });

  if (isLoading || !form) {
    return <Loading rows={4} />;
  }

  return (
    <div style={{ maxWidth: 620 }}>
      <h1>Modifier « {data?.track.title} »</h1>

      <form
        className="card"
        onSubmit={(event) => {
          event.preventDefault();
          updateMutation.mutate();
        }}
      >
        <ErrorMessage error={updateMutation.error} />

        <div className="field">
          <label htmlFor="edit-title">Titre</label>
          <input
            id="edit-title"
            value={form.title}
            required
            maxLength={200}
            onChange={(event) => setForm({ ...form, title: event.target.value })}
          />
        </div>

        <div className="field">
          <label htmlFor="edit-artist">Nom d'artiste</label>
          <input
            id="edit-artist"
            value={form.artistName}
            required
            maxLength={200}
            onChange={(event) => setForm({ ...form, artistName: event.target.value })}
          />
        </div>

        <div className="field">
          <label htmlFor="edit-description">Description</label>
          <textarea
            id="edit-description"
            value={form.description}
            maxLength={5000}
            onChange={(event) => setForm({ ...form, description: event.target.value })}
          />
        </div>

        <div className="row wrap">
          <div className="field grow">
            <label htmlFor="edit-genre">Genre</label>
            <select
              id="edit-genre"
              value={form.genreId}
              onChange={(event) => setForm({ ...form, genreId: event.target.value })}
            >
              <option value="">Non précisé</option>
              {genres?.map((genre) => (
                <option key={genre.id} value={genre.id}>
                  {genre.name}
                </option>
              ))}
            </select>
          </div>

          <div className="field" style={{ width: 120 }}>
            <label htmlFor="edit-year">Année</label>
            <input
              id="edit-year"
              type="number"
              min={1900}
              max={2999}
              value={form.year}
              onChange={(event) => setForm({ ...form, year: event.target.value })}
            />
          </div>

          <div className="field grow">
            <label htmlFor="edit-visibility">Visibilité</label>
            <select
              id="edit-visibility"
              value={form.visibility}
              onChange={(event) => setForm({ ...form, visibility: event.target.value as ContentVisibility })}
            >
              <option value="Public">Public</option>
              <option value="Unlisted">Non répertorié</option>
              <option value="Private">Privé</option>
            </select>
          </div>
        </div>

        <div className="field">
          <label htmlFor="edit-tags">Tags</label>
          <input id="edit-tags" value={form.tags} onChange={(event) => setForm({ ...form, tags: event.target.value })} />
        </div>

        <div className="row">
          <button type="submit" className="btn btn-primary" disabled={updateMutation.isPending}>
            Enregistrer
          </button>
          <button type="button" className="btn" onClick={() => coverInputRef.current?.click()}>
            Changer la pochette
          </button>
          <input
            ref={coverInputRef}
            type="file"
            accept="image/*"
            className="sr-only"
            onChange={(event) => {
              const selected = event.target.files?.[0];
              if (selected) {
                coverMutation.mutate(selected);
              }
            }}
          />
        </div>
      </form>

      <div className="card" style={{ marginTop: 24, borderColor: 'var(--danger)' }}>
        <h2>Supprimer ce morceau</h2>
        <p className="muted small">
          La suppression est définitive : le fichier audio, les pochettes, les likes et les commentaires
          associés seront effacés.
        </p>
        <ErrorMessage error={deleteMutation.error} />
        <button
          type="button"
          className="btn btn-danger"
          disabled={deleteMutation.isPending}
          onClick={() => {
            if (window.confirm(`Supprimer définitivement « ${data?.track.title} » ?`)) {
              deleteMutation.mutate();
            }
          }}
        >
          Supprimer définitivement
        </button>
      </div>
    </div>
  );
}
