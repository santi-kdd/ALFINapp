using System.Security;
using System.Text.RegularExpressions;
using ALFINapp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace ALFINapp.Controllers
{
    public class UserController : Controller
    {
        private readonly MDbContext _context;

        public UserController(MDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearUsuario(Usuario model)
        {
            if (string.IsNullOrWhiteSpace(model.Dni) || !long.TryParse(model.Dni, out _))
            {
                ModelState.AddModelError("dni", "El DNI debe contener solo números.");
            }
            if (ModelState.IsValid)
            {
                var usuarioExistente = await _context.usuarios
                    .FirstOrDefaultAsync(usuario => usuario.Dni == model.Dni);

                if (usuarioExistente != null)
                {
                    ModelState.AddModelError("dni", "El DNI ya está registrado.");
                    return View(model); // Devuelve el modelo con errores
                }

                model.Nombres = model.NombresCompletos;
                _context.usuarios.Add(model);
                TempData["Message"] = "Usuario agregado con exito";
                await _context.SaveChangesAsync();
                return RedirectToAction("Index", "Home");
            }
            else
            {
                return View(model);
            }
        }

        // Acción para verificar si el DNI existe
        [HttpPost]
        public IActionResult VerificarUsuario(string dni)
        {
            if (!dni.All(char.IsDigit))
            {
                TempData["Message"] = "Ingrese Solo Digitos";
                return RedirectToAction("Index", "Home");
            }
            dni = dni.Trim();
            var usuario = _context.usuarios.FirstOrDefault(u => u.Dni.Trim() == dni);


            if (usuario == null)
            {
                TempData["Message"] = "El Usuario a Buscar no se encuentra Registrado comunicarse con X";
                return RedirectToAction("Index", "Home");
            }
            if (string.IsNullOrEmpty(usuario.Rol))
            {
                TempData["Message"] = "El rol del usuario no está definido. Comuníquese con X.";
                return RedirectToAction("Index", "Home");
            }
            if (usuario.Rol == "VENDEDOR")
            {
                HttpContext.Session.SetInt32("UsuarioId", usuario.IdUsuario);
                return RedirectToAction("Ventas");
            }
            if (usuario.Rol == "SUPERVISOR")
            {
                HttpContext.Session.SetInt32("UsuarioId", usuario.IdUsuario);
                return RedirectToAction("VistaMainSupervisor", "Supervisor");
            }
            TempData["Message"] = "Algo salio Mal en la Autenticacion";
            return RedirectToAction("Index", "Home");
        }

        // Acción para mostrar la página de ventas
        [HttpGet]
        public async Task<IActionResult> Ventas()
        {
            int? usuarioId = HttpContext.Session.GetInt32("UsuarioId");
            if (usuarioId == null)
            {
                TempData["Message"] = "Ha ocurrido un error en la autenticacion";
                return RedirectToAction("Index", "Home");
            }
            // Obtener los clientes asignados al usuario
            var clientesAsignados = await _context.clientes_asignados
                                            .Where(ca => ca.IdUsuarioV == usuarioId)
                                            .Select(ca => ca.IdCliente)
                                            .ToListAsync();



            // Obtener las IdBase correspondientes a los clientes asignados
            var clientesGralBase = await (from ce in _context.clientes_enriquecidos
                                          where clientesAsignados.Contains(ce.IdCliente)
                                          select ce.IdBase
                                            ).ToListAsync();
            var clientes = await (from db in _context.detalle_base.AsNoTracking()
                                  join cg in clientesGralBase on db.IdBase equals cg
                                  join bc in _context.base_clientes.AsNoTracking() on db.IdBase equals bc.IdBase
                                  join ca in _context.clientes_asignados on usuarioId equals ca.IdUsuarioV
                                  group new { db, bc, ca } by db.IdBase into grouped
                                  select new
                                  {
                                      IdBase = grouped.Key,
                                      LatestRecord = grouped.OrderByDescending(x => x.db.FechaCarga)
                                                            .FirstOrDefault(),
                                  })
                                  .ToListAsync();

            // Mapear los resultados a DTO
            var detallesClientes = clientes.Select(cliente => new DetalleBaseClienteDTO
            {
                Dni = cliente.LatestRecord?.bc.Dni ?? "",  // Default value in case of null
                XAppaterno = cliente.LatestRecord?.bc.XAppaterno ?? "",
                XApmaterno = cliente.LatestRecord?.bc.XApmaterno ?? "",
                XNombre = cliente.LatestRecord?.bc.XNombre ?? "",
                OfertaMax = cliente.LatestRecord?.db.OfertaMax ?? 0, // Default value in case of null
                Campaña = cliente.LatestRecord?.db.Campaña ?? "",  // Default value in case of null
                IdBase = cliente.IdBase,
                IdAsignacion = cliente.LatestRecord?.ca.IdAsignacion,
                FechaAsignacionVendedor = cliente.LatestRecord?.ca.FechaAsignacionVendedor
            }).ToList();

            // Obtener el usuario actual
            var usuario = await _context.usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.IdUsuario == usuarioId);

            // Asignar el nombre del usuario a la vista
            ViewData["UsuarioNombre"] = usuario != null ? usuario.NombresCompletos : "Usuario No Encontrado";
            return View("Main", detallesClientes);
        }

        [HttpGet]
        public JsonResult VerificarDNI(string dni)
        {
            try
            {
                Console.WriteLine($"DNI recibido: {dni}");
                var clienteExistente = _context.base_clientes.FirstOrDefault(c => c.Dni == dni);
                return Json(new { existe = clienteExistente != null });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al verificar el DNI: {ex.Message}");
                return Json(new { existe = false, error = true, message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult AgregarCliente(BaseCliente model)
        {
            if (ModelState.IsValid)
            {
                if (string.IsNullOrWhiteSpace(model.Dni) ||
                    string.IsNullOrWhiteSpace(model.XAppaterno) ||
                    string.IsNullOrWhiteSpace(model.XApmaterno) ||
                    string.IsNullOrWhiteSpace(model.XNombre) ||
                    model.Edad == null ||
                    string.IsNullOrWhiteSpace(model.Departamento))
                {
                    TempData["Message"] = "Debe llenar todos los campos!";
                    return RedirectToAction("Ventas");
                }

                Regex regexNoNumeros = new Regex(@"^\D+$");

                if (!regexNoNumeros.IsMatch(model.XAppaterno))
                {
                    TempData["Message"] = "El apellido paterno no debe contener números.";
                    return RedirectToAction("Ventas");
                }

                if (!regexNoNumeros.IsMatch(model.XApmaterno))
                {
                    TempData["Message"] = "El apellido materno no debe contener números.";
                    return RedirectToAction("Ventas");
                }

                if (!regexNoNumeros.IsMatch(model.XNombre))
                {
                    TempData["Message"] = "El nombre no debe contener números.";
                    return RedirectToAction("Ventas");
                }

                if (!regexNoNumeros.IsMatch(model.Departamento))
                {
                    TempData["Message"] = "El departamento no debe contener números.";
                    return RedirectToAction("Ventas");
                }

                // Validar que la edad sea un número positivo.
                if (!int.TryParse(model.Edad.ToString(), out int edad) || edad <= 0)
                {
                    TempData["Message"] = "La edad debe ser un número válido y mayor que cero.";
                    return RedirectToAction("Ventas");
                }

                var idSupervisor = (from u in _context.usuarios
                                    where u.IdUsuario == HttpContext.Session.GetInt32("UsuarioId")
                                    select u.IDUSUARIOSUP
                                ).FirstOrDefault();

                if (idSupervisor == null)
                {
                    TempData["Message"] = "Usted no tiene un Supervisor Asignado, no puede agregar clientes.";
                    return RedirectToAction("Ventas");
                }


                _context.base_clientes.Add(model);
                _context.SaveChanges();

                var idBase = model.IdBase;

                ClientesEnriquecido model2 = new ClientesEnriquecido
                {
                    IdBase = idBase,
                    Telefono1 = "0",
                    Telefono2 = "0",
                    Telefono3 = "0",
                    Telefono4 = "0",
                    Telefono5 = "0",
                    FechaEnriquecimiento = DateTime.Now
                };
                _context.clientes_enriquecidos.Add(model2);
                _context.SaveChanges();

                var idCliente = (from ce in _context.clientes_enriquecidos
                                 where ce.IdBase == idBase
                                 select ce.IdCliente
                                ).FirstOrDefault();

                ClientesAsignado model3 = new ClientesAsignado
                {
                    IdCliente = idCliente,
                    IdUsuarioV = HttpContext.Session.GetInt32("UsuarioId") ?? 0,
                    FechaAsignacionVendedor = DateTime.Now,
                    FechaAsignacionSup = null,
                    IdUsuarioS = idSupervisor
                };

                _context.clientes_asignados.Add(model3);
                _context.SaveChanges();

                DetalleBase model4 = new DetalleBase
                {
                    IdBase = idBase,
                    TipoClienteRiegos = "3.NO CLIENTE",
                    FechaCarga = DateTime.Now,
                    TipoBase = "BASE_CAMPO_DIC"
                };

                _context.detalle_base.Add(model4);
                _context.SaveChanges();

                TempData["Message"] = "Cliente agregado con exito!";
                return RedirectToAction("Ventas");
            }
            TempData["Message"] = "El modelo enviado no es valido";
            return RedirectToAction("Ventas");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TipificarCliente(ClientesEnriquecido model)
        {
            var ClientesAsignado = _context.clientes_asignados.
                                        FirstOrDefault(ca => ca.IdCliente == model.IdCliente &&
                                                        ca.IdUsuarioV == HttpContext.Session.GetInt32("UsuarioId"));
            if (ClientesAsignado == null)
            {
                TempData["Message"] = "No tiene permiso para tipificar este cliente";
                return RedirectToAction("Ventas");
            }
            if (ModelState.IsValid)
            {
                var clienteEnriquecido = await _context.clientes_enriquecidos
                    .FirstOrDefaultAsync(c => c.IdBase == model.IdBase);
                if (clienteEnriquecido != null)
                {
                    clienteEnriquecido.Telefono1 = model.Telefono1;
                    clienteEnriquecido.Telefono2 = model.Telefono2;
                    clienteEnriquecido.Telefono3 = model.Telefono3;
                    clienteEnriquecido.Telefono4 = model.Telefono4;
                    clienteEnriquecido.Telefono5 = model.Telefono5;
                    clienteEnriquecido.Email1 = model.Email1;
                    clienteEnriquecido.Email2 = model.Email2;
                    clienteEnriquecido.FechaEnriquecimiento = model.FechaEnriquecimiento;

                    await _context.SaveChangesAsync();
                }
                else
                {
                    TempData["Message"] = "Cliente no encontrado";
                    return RedirectToAction("Ventas");
                }
                TempData["Message"] = "Cliente actualizado con éxito!";
                return RedirectToAction("Ventas");
            }
            return RedirectToAction("Ventas");
        }

        [HttpGet]
        public IActionResult TipificarClienteView(int id_base)
        {
            if (HttpContext.Session.GetInt32("UsuarioId") == null)
            {
                TempData["Message"] = "No ha iniciado sesion";
                return RedirectToAction("Index", "Home");
            }

            Console.WriteLine($"DNI {id_base}");

            var detalleTipificarCliente = (from baseCliente in _context.base_clientes
                                           join detalleBase in _context.detalle_base
                                           on baseCliente.IdBase equals detalleBase.IdBase
                                           join clienteEnriquecido in _context.clientes_enriquecidos
                                           on baseCliente.IdBase equals clienteEnriquecido.IdBase
                                           where baseCliente.IdBase == id_base
                                           select new DetalleTipificarClienteDTO
                                           {
                                               // Propiedades de BaseCliente
                                               Dni = baseCliente.Dni,
                                               XAppaterno = baseCliente.XAppaterno,
                                               XApmaterno = baseCliente.XApmaterno,
                                               XNombre = baseCliente.XNombre,
                                               Edad = baseCliente.Edad,
                                               Departamento = baseCliente.Departamento,
                                               Provincia = baseCliente.Provincia,
                                               Distrito = baseCliente.Distrito,

                                               // Propiedades de DetalleBase
                                               Campaña = detalleBase.Campaña,
                                               OfertaMax = detalleBase.OfertaMax,
                                               TasaMinima = detalleBase.TasaMinima,
                                               Sucursal = detalleBase.Sucursal,
                                               AgenciaComercial = detalleBase.AgenciaComercial,
                                               Plazo = detalleBase.Plazo,
                                               Cuota = detalleBase.Cuota,
                                               GrupoTasa = detalleBase.GrupoTasa,
                                               GrupoMonto = detalleBase.GrupoMonto,
                                               Propension = detalleBase.Propension,
                                               TipoCliente = detalleBase.TipoCliente,
                                               ClienteNuevo = detalleBase.ClienteNuevo,

                                               // Propiedades de ClientesEnriquecido
                                               Telefono1 = clienteEnriquecido.Telefono1,
                                               Telefono2 = clienteEnriquecido.Telefono2,
                                               Telefono3 = clienteEnriquecido.Telefono3,
                                               Telefono4 = clienteEnriquecido.Telefono4,
                                               Telefono5 = clienteEnriquecido.Telefono5
                                           }).FirstOrDefault();

            if (detalleTipificarCliente == null)
            {
                TempData["Message"] = "El Cliente no se le permite ser Tipificado";
                Console.WriteLine("El cliente fue Eliminado manualmente de la Tabla clientes_tipificado");
                return RedirectToAction("Ventas");
            }

            var clienteEnriquecidoConsult = _context.clientes_enriquecidos.FirstOrDefault(ce => ce.IdBase == id_base);
            if (clienteEnriquecidoConsult == null)
            {
                TempData["Message"] = "No se encontró un cliente enriquecido con los criterios proporcionados.";
                return RedirectToAction("Ventas");
            }

            var ClienteAsignado = _context.clientes_asignados.FirstOrDefault(ca => ca.IdCliente == clienteEnriquecidoConsult.IdCliente && ca.IdUsuarioV == HttpContext.Session.GetInt32("UsuarioId"));

            if (ClienteAsignado == null)
            {
                TempData["Message"] = "No tiene permiso para tipificar este cliente";
                return RedirectToAction("Ventas");
            }

            ViewData["ID_asignacion"] = ClienteAsignado.IdAsignacion;

            var tipificaciones = _context.tipificaciones.Select(t => new { t.IdTipificacion, t.DescripcionTipificacion }).ToList();
            ViewData["Tipificaciones"] = tipificaciones;

            var result = (from ct in _context.clientes_tipificados
                          join t in _context.tipificaciones on ct.IdTipificacion equals t.IdTipificacion
                          where ct.IdAsignacion == ClienteAsignado.IdAsignacion &&
                                ct.FechaTipificacion == (
                                    from c in _context.clientes_tipificados
                                    where c.TelefonoTipificado == ct.TelefonoTipificado
                                    select c.FechaTipificacion
                                ).Max()
                          orderby ct.FechaTipificacion descending
                          select new
                          {
                              ct.TelefonoTipificado,
                              t.DescripcionTipificacion,
                              ct.IdAsignacion
                          }).Take(5).ToList();

            var tipificaciones_asignadas = (from t in _context.tipificaciones
                                            join ct in _context.clientes_tipificados on t.IdTipificacion equals ct.IdTipificacion
                                            where ct.IdAsignacion == ClienteAsignado.IdAsignacion
                                            select new
                                            {
                                                t.DescripcionTipificacion,
                                                ct.IdAsignacion
                                            }).ToList();
            ViewData["tipificaciones_asignadas"] = result.ToList();
            var dni = _context.base_clientes.FirstOrDefault(bc => bc.IdBase == id_base);
            ViewData["DNIcliente"] = dni != null ? dni.Dni : "El usuario No tiene DNI Registrado";
            return PartialView("_Tipificarcliente", detalleTipificarCliente);
        }

        [HttpGet]
        public IActionResult AddingClient()
        {
            if (HttpContext.Session.GetInt32("UsuarioId") == null)
            {
                TempData["Message"] = "No ha iniciado sesion";
                return RedirectToAction("Index", "Home");
            }
            Console.WriteLine("Cargando VISTA");
            return PartialView("_AddingClient");
        }

        [HttpGet]
        public IActionResult CerrarSesion()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public IActionResult TipificarMotivo(int IdAsignacion, int? Tipificacion1, int? Tipificacion2, int? Tipificacion3, int? Tipificacion4, int? Tipificacion5, string? Telefono1, string? Telefono2, string? Telefono3, string? Telefono4, string? Telefono5)
        {
            int? usuarioId = HttpContext.Session.GetInt32("UsuarioId");
            if (usuarioId == null)
            {
                TempData["Message"] = "Ha ocurrido un error en la autenticación";
                return RedirectToAction("Index", "Home");
            }

            var ClienteAsignado = _context.clientes_asignados.FirstOrDefault(ca => ca.IdAsignacion == IdAsignacion && ca.IdUsuarioV == HttpContext.Session.GetInt32("UsuarioId"));

            if (ClienteAsignado == null)
            {
                TempData["Message"] = "No tiene permiso para tipificar este cliente";
                Console.WriteLine($"{IdAsignacion}");
                Console.WriteLine($"{Tipificacion1}");
                return RedirectToAction("Ventas");
            }

            var telefonos = new List<string?> { Telefono1, Telefono2, Telefono3, Telefono4, Telefono5 };
            var tipificaciones = new List<int?> { Tipificacion1, Tipificacion2, Tipificacion3, Tipificacion4, Tipificacion5 };

            // Fecha de tipificación
            var fechaTipificacion = DateTime.Now;
            for (int i = 0; i < telefonos.Count; i++)
            {
                var telefono = telefonos[i];
                var tipificacion = tipificaciones[i];

                if (telefono != "0" && tipificacion.HasValue)
                {
                    // Verificar si el teléfono ya está tipificado
                    var clienteTipificado = _context.clientes_tipificados
                        .FirstOrDefault(ct => ct.TelefonoTipificado == telefono);

                    // Agregar un nuevo registro
                    var nuevoClienteTipificado = new ClientesTipificado
                    {
                        IdAsignacion = IdAsignacion,
                        IdTipificacion = tipificacion.Value,
                        FechaTipificacion = fechaTipificacion,
                        Origen = "nuevo",
                        TelefonoTipificado = telefono
                    };
                    _context.clientes_tipificados.Add(nuevoClienteTipificado);

                }
            }

            // Guardar cambios en la base de datos
            _context.SaveChanges();
            TempData["Message"] = "Las Tipificaciones se han guardado Correctamente";

            return RedirectToAction("Ventas");
        }
    }
}