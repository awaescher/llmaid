from .app_settings import AppSettings


class UserManager():
    def __init__(self):
        user_directory = folder_paths.get_user_directory()

        self.settings = AppSettings(self)
        if not os.path.exists(user_directory):
            os.mkdir(user_directory)

        if args.multi_user:
            if os.path.isfile(self.get_users_file()):
                with open(self.get_users_file()) as f:
                    self.users = json.load(f)
            else:
                self.users = {}
        else:
            self.users = {"default": "default"}

    def get_users_file(self):
        return os.path.join(folder_paths.get_user_directory(), "users.json")

    @routes.post("/userdata/{file}/move/{dest}")
    async def move_userdata(request):
        source = get_user_data_path(request, check_exists=True)
        if not isinstance(source, str):
            return source

        dest = get_user_data_path(request, check_exists=False, param="dest")
        if not isinstance(source, str):
            return dest

        overwrite = request.query["overwrite"] != "false"
        if not overwrite and os.path.exists(dest):
            return web.Response(status=409)

        print(f"moving '{source}' -> '{dest}'")
        shutil.move(source, dest)

        resp = os.path.relpath(dest, self.get_request_user_filepath(request, None))
        return web.json_response(resp)
