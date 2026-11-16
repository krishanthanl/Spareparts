export const ACCEPTED_IMAGES = 'image/jpeg,image/png,image/webp';
export const MAX_IMAGE_BYTES = 5 * 1024 * 1024;

/** Quick client-side check; the server decodes and re-checks every upload. */
export function checkImageFile(file: File): string | null {
  if (!ACCEPTED_IMAGES.split(',').includes(file.type))
    return 'Choose a JPEG, PNG or WebP image.';
  if (file.size > MAX_IMAGE_BYTES) return 'The image must be 5 MB or smaller.';
  return null;
}
