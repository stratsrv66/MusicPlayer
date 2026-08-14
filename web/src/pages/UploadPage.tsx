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

/**
 * Import d'un morceau.
 *
 * L'envoi utilise `XMLHttpRequest` plutôt que `fetch` : c'est la seule API du
 * navigateur qui expose la progression de l'upload, indispensable pour un fichier
 * pouvant atteindre 20 Mo.
 */
export function UploadPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { data: genres } = useQuery({ queryKey: ['genres'], queryFn: discoveryApi.genres, staleTime: 300000 });

  const [file, setFile] = useState<File | null>(null);
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

  function validate(): string | null {
    if (!file) {
      return 'Sélectionnez un fichier audio.';
    }
    if (file.size > MAX_FILE_BYTES) {
      return `Le fichier dépasse la taille maximale de ${formatBytes(MAX_FILE_BYTES)}.`;
    }
    return null;
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);

    const validationMessage = validate();
    if (validationMessage) {
      setError(new ApiError(400, { detail: validationMessage }));
      return;
    }

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

    setUploading(true);
    setProgress(0);

    try {
      const result = await uploadWithProgress(form, setProgress);
      setAccepted(result);

      if (cover) {
        await tracksApi.setCover(result.trackId, cover);
      }

      queryClient.invalidateQueries({ queryKey: ['my-tracks'] });
    } catch (caught) {
      setError(caught);
      setUploading(false);
    }
  }

  return (
    <>
      <h1>Importer un morceau</h1>

      {accepted ? (
        <ProcessingStatus trackId={accepted.trackId} onReady={() => navigate(`/tracks/${accepted.trackId}`)} />
      ) : (
        <form onSubmit={handleSubmit} className="card" style={{ maxWidth: 620 }}>
          <ErrorMessage error={error} />

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
              Sans pochette fournie, celle intégrée au fichier audio sera utilisée.
            </span>
          </div>

          {uploading && (
            <div className="field">
              <label htmlFor="upload-progress">Envoi en cours</label>
              <div className="progress-bar" id="upload-progress" role="progressbar" aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}>
                <div style={{ width: `${progress}%` }} />
              </div>
              <span className="small muted">{progress} %</span>
            </div>
          )}

          <button type="submit" className="btn btn-primary" disabled={uploading || !file}>
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
