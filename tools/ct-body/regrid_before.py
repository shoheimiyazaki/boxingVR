"""New hand-made body for the CT build at the new grids: generated parts at the new cell counts, with the old
hand-edited head (face shell) resampled onto the new head grid (nearest cell), keeping the shell one cell thick."""
import sys, numpy as np
sys.path.insert(0, sys.argv[1])
import vox
from scipy import ndimage
SKIN, HAIR, LIP, MUSCLE = 1, 14, 21, 2
def load(p):
    ms, pal = vox.read(p)
    return {m.name: vox.from_vox_axes(m.cells) for m in ms}, pal
def outer(filled):
    pad = np.pad(filled, 1)
    return filled & ~ndimage.binary_erosion(pad, structure=ndimage.generate_binary_structure(3, 1))[1:-1, 1:-1, 1:-1]
old, _ = load(sys.argv[2]); gen, pal = load(sys.argv[3])
h = old['Head']; dims = gen['Head'].shape
idx = [np.minimum(((np.arange(n) + 0.5) * h.shape[a] / n).astype(int), h.shape[a] - 1) for a, n in enumerate(dims)]
ii, jj, kk = np.meshgrid(*idx, indexing='ij')
head = h[ii, jj, kk]
src_outer = outer(h != 0)[ii, jj, kk]
new_outer = outer(head != 0)
# a covering cell copied inward by the stretch (its source was on the surface, it is not) becomes flesh
thick = np.isin(head, [SKIN, HAIR, LIP]) & src_outer & ~new_outer
head[thick] = MUSCLE
# the stretch can open the surface: an inner non-shell cell now on the surface becomes skin
bare = new_outer & (head != 0) & ~np.isin(head, [SKIN, HAIR, LIP, 9, 15])
head[bare] = SKIN
print('head', h.shape, '->', dims, 'thinned', int(thick.sum()), 'covered', int(bare.sum()))
for m in np.unique(head): print('  mat', m, int((head == m).sum()), 'old', int((h == m).sum()))
models = [vox.VoxModel(n, vox.to_vox_axes(head if n == 'Head' else g)) for n, g in gen.items()]
vox.write(sys.argv[4], models, pal)
