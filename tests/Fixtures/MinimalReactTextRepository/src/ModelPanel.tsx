export function ModelPanel({ brand, name }: { readonly brand: string; readonly name: string }) {
  const link = `https://wa.me/59100000000?text=${encodeURIComponent(`Hola, quiero más información sobre el ${brand} ${name}`)}`;
  return (
    <section className="model-panel flex items-center gap-2" id="propuesta" data-slide="Not shown">
      <a href={link} target="_blank" rel="noreferrer" className="arena-size-lg">
        Solicitar información
      </a>
      <button type="submit" title="Explorar el modelo" aria-label={'Abrir la ficha del modelo'}>
        Calcular
      </button>
      <img src="/img/cover photo.png" alt="Portada del modelo" />
      {'Ver detalles'}
    </section>
  );
}
