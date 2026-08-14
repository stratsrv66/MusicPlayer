import { useEffect, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { meApi } from '../services/api';
import { apiUrl, mediaUrl } from '../services/apiClient';
import { readTokens } from '../services/tokenStore';
import { useAuthStore } from '../features/auth/authStore';
import { Empty, ErrorMessage, Loading } from '../components/common';
import { formatBytes, formatDateTime } from '../lib/format';
import type { ProfileVisibility } from '../types/api';

/** Intervalle d'interrogation de l'état d'un export en cours, en millisecondes. */
const EXPORT_POLL_MS = 2000;

/** Paramètres du compte : profil, préférences, export et suppression. */
export function SettingsPage() {
  const me = useAuthStore((state) => state.me);
  const refreshAuth = useAuthStore((state) => state.refresh);
  const queryClient = useQueryClient();

  const [username, setUsername] = useState('');
  const [bio, setBio] = useState('');
  const [visibility, setVisibility] = useState<ProfileVisibility>('Public');
  const [socialLinks, setSocialLinks] = useState<{ label: string; url: string }[]>([]);
  const [saved, setSaved] = useState(false);

  // Le formulaire est renseigné dès que le profil est disponible.
  useEffect(() => {
    if (me) {
      setUsername(me.profile.username);
      setBio(me.profile.bio ?? '');
      setVisibility(me.profile.profileVisibility);
      setSocialLinks(Object.entries(me.profile.socialLinks ?? {}).map(([label, url]) => ({ label, url })));
    }
  }, [me]);

  const profileMutation = useMutation({
    mutationFn: () =>
      meApi.update({
        username,
        bio,
        profileVisibility: visibility,
        socialLinks: Object.fromEntries(
          socialLinks.filter((link) => link.label.trim() && link.url.trim()).map((link) => [link.label.trim(), link.url.trim()]),
        ),
      }),
    onSuccess: async () => {
      await refreshAuth();
      setSaved(true);
    },
  });

  const avatarMutation = useMutation({
    mutationFn: (file: File) => meApi.setAvatar(file),
    onSuccess: () => refreshAuth(),
  });

  const removeAvatarMutation = useMutation({
    mutationFn: () => meApi.removeAvatar(),
    onSuccess: () => refreshAuth(),
  });

  const settingsMutation = useMutation({
    mutationFn: (body: { showLikeCount?: boolean; showPlayCount?: boolean }) => meApi.updateSettings(body),
    onSuccess: () => {
      refreshAuth();
      queryClient.invalidateQueries({ queryKey: ['tracks'] });
    },
  });

  if (!me) {
    return <Loading rows={3} />;
  }

  function handleProfileSubmit(event: FormEvent) {
    event.preventDefault();
    setSaved(false);
    profileMutation.mutate();
  }

  const avatar = mediaUrl(me.profile.avatarUrl);

  return (
    <div style={{ maxWidth: 680 }}>
      <h1>Paramètres</h1>

      <section className="card section">
        <h2>Profil</h2>
        <ErrorMessage error={profileMutation.error ?? avatarMutation.error} />
        {saved && <div className="alert alert-success" role="status">Profil enregistré.</div>}

        <div className="row" style={{ marginBottom: 16 }}>
          {avatar ? <img className="avatar avatar-lg" src={avatar} alt="Votre avatar" /> : <span className="avatar avatar-lg" />}
          <div className="stack">
            <div className="field" style={{ marginBottom: 0 }}>
              <label htmlFor="settings-avatar">Changer l'avatar</label>
              <input
                id="settings-avatar"
                type="file"
                accept="image/*"
                onChange={(event) => {
                  const file = event.target.files?.[0];
                  if (file) {
                    avatarMutation.mutate(file);
                  }
                }}
              />
            </div>
            {me.profile.avatarUrl && (
              <button type="button" className="btn btn-sm" onClick={() => removeAvatarMutation.mutate()}>
                Supprimer l'avatar
              </button>
            )}
          </div>
        </div>

        <form onSubmit={handleProfileSubmit}>
          <div className="field">
            <label htmlFor="settings-username">Nom d'utilisateur</label>
            <input
              id="settings-username"
              value={username}
              required
              minLength={3}
              maxLength={32}
              onChange={(event) => setUsername(event.target.value)}
            />
          </div>

          <div className="field">
            <label htmlFor="settings-bio">Biographie</label>
            <textarea
              id="settings-bio"
              value={bio}
              maxLength={1000}
              onChange={(event) => setBio(event.target.value)}
              aria-describedby="settings-bio-help"
            />
            <span id="settings-bio-help" className="small muted">
              {bio.length} / 1000 caractères
            </span>
          </div>

          <div className="field">
            <label htmlFor="settings-visibility">Visibilité du profil</label>
            <select
              id="settings-visibility"
              value={visibility}
              onChange={(event) => setVisibility(event.target.value as ProfileVisibility)}
              aria-describedby="settings-visibility-help"
            >
              <option value="Public">Public</option>
              <option value="Private">Privé</option>
            </select>
            <span id="settings-visibility-help" className="small muted">
              Un profil privé masque vos morceaux, playlists et abonnements aux autres utilisateurs.
            </span>
          </div>

          <fieldset style={{ border: 'none', padding: 0, margin: '0 0 16px' }}>
            <legend className="small muted" style={{ padding: 0, marginBottom: 8 }}>
              Liens sociaux (8 maximum)
            </legend>

            {socialLinks.map((link, index) => (
              <div key={index} className="row" style={{ marginBottom: 8 }}>
                <input
                  aria-label={`Libellé du lien ${index + 1}`}
                  placeholder="Libellé"
                  value={link.label}
                  maxLength={32}
                  style={{ maxWidth: 160 }}
                  onChange={(event) => {
                    const next = [...socialLinks];
                    next[index] = { ...next[index], label: event.target.value };
                    setSocialLinks(next);
                  }}
                />
                <input
                  aria-label={`Adresse du lien ${index + 1}`}
                  placeholder="https://…"
                  type="url"
                  value={link.url}
                  onChange={(event) => {
                    const next = [...socialLinks];
                    next[index] = { ...next[index], url: event.target.value };
                    setSocialLinks(next);
                  }}
                />
                <button
                  type="button"
                  className="btn btn-sm"
                  onClick={() => setSocialLinks(socialLinks.filter((_, i) => i !== index))}
                  aria-label={`Supprimer le lien ${index + 1}`}
                >
                  ✕
                </button>
              </div>
            ))}

            {socialLinks.length < 8 && (
              <button
                type="button"
                className="btn btn-sm"
                onClick={() => setSocialLinks([...socialLinks, { label: '', url: '' }])}
              >
                Ajouter un lien
              </button>
            )}
          </fieldset>

          <button type="submit" className="btn btn-primary" disabled={profileMutation.isPending}>
            Enregistrer
          </button>
        </form>
      </section>

      <section className="card section">
        <h2>Confidentialité des statistiques</h2>
        <p className="small muted">
          Masquer un compteur le retire de l'affichage public. Il reste visible dans votre tableau de bord.
        </p>
        <ErrorMessage error={settingsMutation.error} />

        <div className="field checkbox">
          <input
            id="show-likes"
            type="checkbox"
            checked={me.settings.showLikeCount}
            onChange={(event) => settingsMutation.mutate({ showLikeCount: event.target.checked })}
          />
          <label htmlFor="show-likes">Afficher publiquement le nombre de likes</label>
        </div>

        <div className="field checkbox">
          <input
            id="show-plays"
            type="checkbox"
            checked={me.settings.showPlayCount}
            onChange={(event) => settingsMutation.mutate({ showPlayCount: event.target.checked })}
          />
          <label htmlFor="show-plays">Afficher publiquement le nombre d'écoutes</label>
        </div>
      </section>

      <DataExportSection />

      <DeleteAccountSection username={me.profile.username} />
    </div>
  );
}

/** Demande et téléchargement des exports de données personnelles. */
function DataExportSection() {
  const queryClient = useQueryClient();

  const { data, isLoading, error } = useQuery({
    queryKey: ['exports'],
    queryFn: () => meApi.exports({ pageSize: 10 }),
    // On interroge tant qu'un export est en cours de génération.
    refetchInterval: (query) =>
      query.state.data?.items.some((item) => item.status === 'Pending' || item.status === 'Processing')
        ? EXPORT_POLL_MS
        : false,
  });

  const requestMutation = useMutation({
    mutationFn: () => meApi.requestExport(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['exports'] }),
  });

  /**
   * Télécharge l'archive.
   * Le lien direct ne conviendrait pas : l'endpoint exige l'en-tête d'autorisation,
   * l'archive est donc récupérée puis exposée via une URL d'objet éphémère.
   */
  async function download(exportId: string) {
    const token = readTokens()?.accessToken;
    const response = await fetch(apiUrl(`/me/data-exports/${exportId}/download`), {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });

    if (!response.ok) {
      return;
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `musicplatform-export-${exportId}.zip`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <section className="card section">
      <h2>Exporter mes données</h2>
      <p className="small muted">
        L'archive contient votre profil, vos morceaux, playlists, likes, commentaires, abonnements et
        historique d'écoute. Elle reste disponible sept jours.
      </p>

      <ErrorMessage error={error ?? requestMutation.error} />

      <button
        type="button"
        className="btn"
        onClick={() => requestMutation.mutate()}
        disabled={requestMutation.isPending}
        style={{ marginBottom: 16 }}
      >
        Demander un export
      </button>

      {isLoading ? (
        <Loading rows={1} />
      ) : data && data.items.length > 0 ? (
        <div className="table-wrapper">
          <table>
            <caption className="sr-only">Historique de vos demandes d'export</caption>
            <thead>
              <tr>
                <th scope="col">Demandé le</th>
                <th scope="col">État</th>
                <th scope="col">Taille</th>
                <th scope="col">Expire le</th>
                <th scope="col">Action</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((item) => (
                <tr key={item.id}>
                  <td>{formatDateTime(item.createdAt)}</td>
                  <td>
                    <span className={`badge${item.status === 'Ready' ? ' badge-success' : item.status === 'Failed' ? ' badge-danger' : ''}`}>
                      {item.status}
                    </span>
                  </td>
                  <td>{item.fileSize ? formatBytes(item.fileSize) : '—'}</td>
                  <td>{item.expiresAt ? formatDateTime(item.expiresAt) : '—'}</td>
                  <td>
                    {item.status === 'Ready' ? (
                      <button type="button" className="btn btn-sm" onClick={() => void download(item.id)}>
                        Télécharger
                      </button>
                    ) : (
                      <span className="muted small">—</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <Empty>Aucun export demandé.</Empty>
      )}
    </section>
  );
}

/** Suppression du compte, avec avertissement et confirmation explicite. */
function DeleteAccountSection({ username }: { username: string }) {
  const [confirmation, setConfirmation] = useState('');
  const clearSession = useAuthStore((state) => state.clear);
  const navigate = useNavigate();

  const mutation = useMutation({
    mutationFn: () => meApi.deleteAccount(confirmation),
    onSuccess: () => {
      clearSession();
      navigate('/');
    },
  });

  return (
    <section className="card section" style={{ borderColor: 'var(--danger)' }}>
      <h2>Supprimer mon compte</h2>

      <div className="alert alert-error" role="alert">
        <strong>Cette action est irréversible.</strong> Vos morceaux, fichiers audio, pochettes, playlists,
        likes, commentaires et abonnements seront définitivement supprimés. Pensez à demander un export
        de vos données avant de continuer.
      </div>

      <ErrorMessage error={mutation.error} />

      <div className="field">
        <label htmlFor="delete-confirm">
          Saisissez votre nom d'utilisateur « {username} » pour confirmer
        </label>
        <input
          id="delete-confirm"
          value={confirmation}
          onChange={(event) => setConfirmation(event.target.value)}
          autoComplete="off"
        />
      </div>

      <button
        type="button"
        className="btn btn-danger"
        disabled={confirmation !== username || mutation.isPending}
        onClick={() => {
          if (window.confirm('Supprimer définitivement votre compte ? Cette action est irréversible.')) {
            mutation.mutate();
          }
        }}
      >
        Supprimer définitivement mon compte
      </button>
    </section>
  );
}
