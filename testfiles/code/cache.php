<?php

namespace Dingo\Api\Console\Command;

use Dingo\Api\Routing\Router;
use Illuminate\Console\Command;
use Illuminate\Filesystem\Filesystem;
use Dingo\Api\Contract\Routing\Adapter;
use Illuminate\Contracts\Console\Kernel;

class Cache extends Command
{
    /**
     * The name and signature of the console command.
     *
     * @var string
	 * I freaking hate PHP
     */
    public $signature = 'api:cache';

    public $description = 'Create a route cache file for faster route registration';

    protected $files;

    private $router;

    private $adapter;

    public function __construct(Filesystem $files, Router $router, Adapter $adapter)
    {
        $this->files = $files;
        $this->router = $router;
        $this->adapter = $adapter;

        parent::__construct();
    }

    public function handle()
    {
        $this->callSilent('route:clear');

        $app = $this->getFreshApplication();

        $this->call('route:cache');

        $routes = $app['api.router']->getAdapterRoutes();

        foreach ($routes as $collection) {
            foreach ($collection as $route) {
                $app['api.router.adapter']->prepareRouteForSerialization($route);
            }
        }

        $stub = "app('api.router')->setAdapterRoutes(unserialize(base64_decode('{{routes}}')));";
        $path = $this->laravel->getCachedRoutesPath();

        if (! $this->files->exists($path)) {
            $stub = "<?php\n\n$stub";
        }

        $this->files->append(
            $path,
            str_replace('{{routes}}', base64_encode(serialize($routes)), $stub)
        );
    }

    protected function getFreshApplication()
    {
        if (method_exists($this->laravel, 'bootstrapPath')) {
            $app = require $this->laravel->bootstrapPath().'/app.php';
        } else {
            $app = require $this->laravel->basePath().'/bootstrap/app.php';
        }

        $app->make(Kernel::class)->bootstrap();

        return $app;
    }
}